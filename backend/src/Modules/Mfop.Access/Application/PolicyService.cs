using System.Globalization;
using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

/// <summary>
/// Security policies (docs/lean-pilot/05 §5.9) as data: seeded with defaults by the migrator (audited, SYSTEM), changed by the
/// admin with a reason (audited), range-validated. Durations such as the offline grace period are never hard-coded.
/// </summary>
public sealed class PolicyService(AccessDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock) : IPolicies
{
    public Task<Result<int>> EnsureDefaultsAsync(CancellationToken ct) =>
        uow.ExecuteAsync<int>(CommandEnvelope.For("EnsurePolicyDefaults", ActorContext.System, "DEFAULTS"), async innerCt =>
        {
            var existing = await db.PolicySettings.Select(p => p.Key).ToListAsync(innerCt);
            var added = 0;
            foreach (var definition in PolicyKeys.All.Where(d => !existing.Contains(d.Key)))
            {
                var setting = new PolicySetting(Ids.New(), definition.Key, definition.Default, null, clock.UtcNowMicros());
                db.PolicySettings.Add(setting);
                audit.Record(setting, "policy_setting.created");
                added++;
            }

            return added;
        }, ct);

    public async Task<IReadOnlyList<PolicySettingDto>> ListAsync(CancellationToken ct)
    {
        var settings = await db.PolicySettings.AsNoTracking().ToDictionaryAsync(p => p.Key, ct);
        return PolicyKeys.All.Where(d => settings.ContainsKey(d.Key)).Select(d =>
        {
            var s = settings[d.Key];
            return new PolicySettingDto(d.Key, s.Value, d.Default, d.Description, s.ChangedBy, s.ChangedAt, s.Version);
        }).ToList();
    }

    public Task<Result<Guid>> ChangeAsync(CommandEnvelope cmd, string key, ChangePolicyRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (PolicyKeys.Find(key) is null) return Error.NotFound("POLICY_NOT_FOUND", $"Unknown policy '{key}'.");
            if (string.IsNullOrWhiteSpace(request.Reason)) return Error.Validation("REASON_REQUIRED", "A reason is required to change a policy.");
            if (string.IsNullOrWhiteSpace(request.Value)) return Error.Validation("INVALID_POLICY_VALUE", "A value is required.");
            var setting = await db.PolicySettings.SingleOrDefaultAsync(p => p.Key == key, innerCt);
            if (setting is null) return Error.NotFound("POLICY_NOT_FOUND", $"Policy '{key}' is not seeded.");
            var changed = setting.Change(request.Value, cmd.Actor.UserId!.Value, clock.UtcNowMicros());
            if (!changed.IsSuccess) return changed.Error!;
            audit.Record(setting, "policy_setting.changed", request.Reason.Trim());
            return setting.Id;
        }, ct);

    public async Task<string> ValueAsync(string key, CancellationToken ct) =>
        await db.PolicySettings.AsNoTracking().Where(p => p.Key == key).Select(p => p.Value).SingleOrDefaultAsync(ct)
        ?? PolicyKeys.Find(key)?.Default
        ?? throw new InvalidOperationException($"Unknown policy {key}");

    public async Task<int> IntAsync(string key, CancellationToken ct) => int.Parse(await ValueAsync(key, ct), CultureInfo.InvariantCulture);

    public async Task<ClientPolicyDto> ClientPolicyAsync(CancellationToken ct) => new(
        await IntAsync(PolicyKeys.OfflineGraceHours, ct), await IntAsync(PolicyKeys.AppLockMinutes, ct), await ValueAsync(PolicyKeys.MinAppVersion, ct));
}
