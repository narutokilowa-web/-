using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Rules.Contracts;
using Mfop.Rules.Domain;
using Mfop.Rules.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Rules.Application;

/// <summary>Rule settings are data: seeded once (audited, actor SYSTEM) and changed only by ADMIN (audited, old → new).</summary>
public sealed class RuleSettingsService(RulesDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock)
{
    /// <summary>Default parameters written when the setting does not exist yet (migrator bootstrap).</summary>
    public static readonly RepeatedFailureParams RepeatedFailureDefaults = new(Enabled: true, Threshold: 3, WindowDays: 180);

    public async Task<IReadOnlyList<RuleSettingDto>> ListAsync(CancellationToken ct) =>
        (await db.Settings.AsNoTracking().OrderBy(s => s.RuleCode).ToListAsync(ct)).Select(ToDto).ToList();

    public Task<Result<Guid>> EnsureDefaultsAsync(CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(CommandEnvelope.For("EnsureRuleDefaults", ActorContext.System, "BOOTSTRAP_DEFAULTS"), async innerCt =>
        {
            var existing = await db.Settings.SingleOrDefaultAsync(s => s.RuleCode == RuleCodes.RepeatedFailure, innerCt);
            if (existing is not null) return existing.Id;
            var setting = new RuleSetting(Ids.New(), RuleCodes.RepeatedFailure, Serialize(RepeatedFailureDefaults), RepeatedFailureDefaults.Enabled, null, clock.UtcNowMicros());
            db.Settings.Add(setting);
            audit.Record(setting, "rule_setting.created");
            return setting.Id;
        }, ct);

    /// <summary>policy.settings.change (ADMIN). Takes effect for the next evaluation, without any code change.</summary>
    public Task<Result<Guid>> ChangeRepeatedFailureAsync(CommandEnvelope cmd, ChangeRepeatedFailureSettingRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (request.Threshold is < 1 or > 100) return Error.Validation("INVALID_THRESHOLD", "The threshold must be between 1 and 100.");
            if (request.WindowDays is < 1 or > 3650) return Error.Validation("INVALID_WINDOW", "The window must be between 1 and 3650 days.");
            if (string.IsNullOrWhiteSpace(request.Reason)) return Error.Validation("REASON_REQUIRED", "A reason is required to change a rule setting.");
            var setting = await db.Settings.SingleOrDefaultAsync(s => s.RuleCode == RuleCodes.RepeatedFailure, innerCt);
            if (setting is null) return Error.NotFound("RULE_SETTING_NOT_FOUND", "The rule setting does not exist.");
            setting.Change(Serialize(new RepeatedFailureParams(request.Enabled, request.Threshold, request.WindowDays)), request.Enabled, cmd.Actor.UserId!.Value, clock.UtcNowMicros());
            audit.Record(setting, "rule_setting.changed", request.Reason.Trim());
            return setting.Id;
        }, ct);

    public static RepeatedFailureParams ParseRepeatedFailure(RuleSetting setting)
    {
        using var doc = JsonDocument.Parse(setting.Params);
        var root = doc.RootElement;
        return new RepeatedFailureParams(setting.Enabled, root.GetProperty("threshold").GetInt32(), root.GetProperty("window_days").GetInt32());
    }

    private static string Serialize(RepeatedFailureParams p) =>
        new JsonObject { ["threshold"] = p.Threshold, ["window_days"] = p.WindowDays }.ToJsonString();

    private static RuleSettingDto ToDto(RuleSetting s) =>
        new(s.RuleCode, s.Enabled, JsonDocument.Parse(s.Params).RootElement.Clone(), s.ChangedBy, s.ChangedAt, s.Version);
}

/// <summary>The minimal repeated-failure rule (docs/lean-pilot/11 §11.2 M3 item 10). Reads its parameters from rule_settings.</summary>
public sealed class RepeatedFailureRule(RulesDbContext db, IAuditTrail audit, TimeProvider clock) : IRepeatedFailureRule, IRecommendations
{
    public async Task<RepeatedFailureEvaluation> EvaluateAsync(Guid assetId, IReadOnlyList<ResolvedFailure> resolved, CancellationToken ct)
    {
        var setting = await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.RuleCode == RuleCodes.RepeatedFailure, ct);
        if (setting is null)
        {
            return new RepeatedFailureEvaluation(false, 0, 0, 0, null); // not configured: the rule does not run
        }

        var parameters = RuleSettingsService.ParseRepeatedFailure(setting);
        var now = clock.UtcNowMicros();
        var outcome = RepeatedFailureEvaluator.Evaluate(
            parameters,
            resolved.Select(r => new FailureCandidate(r.FaultId, r.WorkOrderId, r.WorkOrderCode, r.DiagnosisCode, r.CountsAsFailure, r.ResolvedAt)),
            now);
        if (!outcome.Fires)
        {
            return new RepeatedFailureEvaluation(false, outcome.Count, parameters.Threshold, parameters.WindowDays, null);
        }

        var explanation = Explanation(parameters, outcome);
        var open = await db.Recommendations.SingleOrDefaultAsync(r => r.AssetId == assetId && r.State == RecommendationStates.Open, ct);
        if (open is null)
        {
            open = new Recommendation(Ids.New(), assetId, RuleCodes.RepeatedFailure, explanation, now);
            db.Recommendations.Add(open);
            audit.Record(open, "recommendation.created", extra: Facts(parameters, outcome));
        }
        else
        {
            open.Refresh(explanation, now);
            audit.Record(open, "recommendation.refreshed", extra: Facts(parameters, outcome));
        }

        return new RepeatedFailureEvaluation(true, outcome.Count, parameters.Threshold, parameters.WindowDays, open.Id);
    }

    public async Task<IReadOnlyList<RecommendationDto>> OpenForAssetAsync(Guid assetId, CancellationToken ct) =>
        (await db.Recommendations.AsNoTracking().Where(r => r.AssetId == assetId && r.State == RecommendationStates.Open).ToListAsync(ct))
        .Select(ToDto).ToList();

    public static RecommendationDto ToDto(Recommendation r)
    {
        using var doc = JsonDocument.Parse(r.Explanation);
        var root = doc.RootElement;
        var counted = root.GetProperty("faults").EnumerateArray().Select(f => new CountedFailure(
            Guid.Parse(f.GetProperty("fault_id").GetString()!), Guid.Parse(f.GetProperty("work_order_id").GetString()!),
            f.GetProperty("work_order_code").GetString()!, f.GetProperty("diagnosis_code").GetString()!,
            DateTimeOffset.Parse(f.GetProperty("resolved_at").GetString()!, CultureInfo.InvariantCulture))).ToList();
        return new RecommendationDto(r.Id, r.AssetId, r.Code, r.RuleCode, r.State, root.GetProperty("count").GetInt32(),
            root.GetProperty("threshold").GetInt32(), root.GetProperty("window_days").GetInt32(), counted, r.CreatedAt, r.UpdatedAt);
    }

    private static string Explanation(RepeatedFailureParams parameters, RepeatedFailureOutcome outcome)
    {
        var faults = new JsonArray();
        foreach (var c in outcome.Counted)
        {
            faults.Add(new JsonObject
            {
                ["fault_id"] = c.FaultId.ToString("D"),
                ["work_order_id"] = c.WorkOrderId.ToString("D"),
                ["work_order_code"] = c.WorkOrderCode,
                ["diagnosis_code"] = c.DiagnosisCode,
                ["resolved_at"] = c.ResolvedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            });
        }

        return new JsonObject
        {
            ["rule"] = RuleCodes.RepeatedFailure,
            ["threshold"] = parameters.Threshold,
            ["window_days"] = parameters.WindowDays,
            ["count"] = outcome.Count,
            ["faults"] = faults,
        }.ToJsonString();
    }

    private static Dictionary<string, object?> Facts(RepeatedFailureParams parameters, RepeatedFailureOutcome outcome) => new()
    {
        ["rule"] = RuleCodes.RepeatedFailure,
        ["threshold"] = parameters.Threshold,
        ["window_days"] = parameters.WindowDays,
        ["count"] = outcome.Count,
        ["work_orders"] = string.Join(",", outcome.Counted.Select(c => c.WorkOrderCode)),
    };
}

public sealed class FlagService(RulesDbContext db, IAuditTrail audit, TimeProvider clock) : IFlags
{
    private static readonly HashSet<string> Severities = [FlagSeverities.Info, FlagSeverities.Warning, FlagSeverities.Critical];

    public Task<Guid> RaiseAsync(string ruleCode, string severity, string subjectType, Guid subjectId, IReadOnlyDictionary<string, object?> explanation, CancellationToken ct)
    {
        if (!Severities.Contains(severity)) throw new ArgumentException($"Unknown severity {severity}.", nameof(severity));
        var json = new JsonObject();
        foreach (var (key, value) in explanation.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            json[key] = value switch
            {
                null => null,
                string s => s,
                bool b => b,
                int i => i,
                long l => l,
                _ => throw new ArgumentException($"Unsupported explanation value for {key}.", nameof(explanation)),
            };
        }

        var flag = new Flag(Ids.New(), ruleCode, severity, subjectType, subjectId, json.ToJsonString(), clock.UtcNowMicros());
        db.Flags.Add(flag);
        audit.Record(flag, "flag.raised", extra: new Dictionary<string, object?> { ["rule_code"] = ruleCode, ["severity"] = severity });
        return Task.FromResult(flag.Id);
    }

    public async Task<IReadOnlyList<FlagDto>> ForSubjectsAsync(string subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct) =>
        (await db.Flags.AsNoTracking().Where(f => f.SubjectType == subjectType && subjectIds.Contains(f.SubjectId)).OrderBy(f => f.CreatedAt).ToListAsync(ct))
        .Select(f => new FlagDto(f.Id, f.RuleCode, f.Severity, f.SubjectType, f.SubjectId, f.State, JsonDocument.Parse(f.Explanation).RootElement.Clone(), f.CreatedAt))
        .ToList();
}
