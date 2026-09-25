using Mfop.Kernel.Auditing;

namespace Mfop.Rules.Domain;

public static class RuleCodes
{
    /// <summary>The single analytics rule in DEMO 1 (docs/lean-pilot/11 §11.2 M3 item 10).</summary>
    public const string RepeatedFailure = "REPEATED_FAILURE";
}

/// <summary>
/// Parameters of a rule, stored as data (docs/lean-pilot/02: rule_settings). Nothing in the business logic hard-codes
/// the values; the defaults are seeded by the migrator and changed only by ADMIN through an audited command.
/// </summary>
public sealed class RuleSetting : IAuditable
{
    private RuleSetting() { }

    public RuleSetting(Guid id, string ruleCode, string parameters, bool enabled, Guid? changedBy, DateTimeOffset changedAt)
    {
        Id = id;
        RuleCode = ruleCode;
        Params = parameters;
        Enabled = enabled;
        ChangedBy = changedBy;
        ChangedAt = changedAt;
    }

    public string AuditEntityType => "rule_setting";

    public Guid Id { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;

    /// <summary>JSON object, e.g. {"threshold":3,"window_days":180}.</summary>
    public string Params { get; private set; } = "{}";
    public bool Enabled { get; private set; }
    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public int Version { get; private set; }

    public void Change(string parameters, bool enabled, Guid changedBy, DateTimeOffset now)
    {
        Params = parameters;
        Enabled = enabled;
        ChangedBy = changedBy;
        ChangedAt = now;
    }
}

public static class RecommendationStates
{
    public const string Open = "OPEN";
}

/// <summary>
/// "Recurrent Failure — Recommendation: Full inspection recommended". A recommendation, never a verdict.
/// ACCEPTED/DISMISSED decisions come later (M6); in DEMO 1 it stays OPEN and is refreshed on later evaluations.
/// </summary>
public sealed class Recommendation : IAuditable
{
    public const string FullInspection = "FULL_INSPECTION";

    private Recommendation() { }

    public Recommendation(Guid id, Guid assetId, string ruleCode, string explanation, DateTimeOffset now)
    {
        Id = id;
        AssetId = assetId;
        Code = FullInspection;
        RuleCode = ruleCode;
        Explanation = explanation;
        State = RecommendationStates.Open;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string AuditEntityType => "recommendation";

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public string Code { get; private set; } = FullInspection;
    public string RuleCode { get; private set; } = RuleCodes.RepeatedFailure;

    /// <summary>JSON: the counted faults, the threshold and the window that produced the recommendation.</summary>
    public string Explanation { get; private set; } = "{}";
    public string State { get; private set; } = RecommendationStates.Open;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public void Refresh(string explanation, DateTimeOffset now)
    {
        Explanation = explanation;
        UpdatedAt = now;
    }
}

public static class FlagSeverities
{
    public const string Info = "INFO", Warning = "WARNING", Critical = "CRITICAL";
}

/// <summary>
/// An integrity flag: "flags, not verdicts" (AGENTS.md). Raised by rules in other modules; shown to the reviewer.
/// The auditor's decision workflow (UNDER_REVIEW / CLOSED) is M6.
/// </summary>
public sealed class Flag : IAuditable
{
    private Flag() { }

    public Flag(Guid id, string ruleCode, string severity, string subjectType, Guid subjectId, string explanation, DateTimeOffset now)
    {
        Id = id;
        RuleCode = ruleCode;
        Severity = severity;
        SubjectType = subjectType;
        SubjectId = subjectId;
        Explanation = explanation;
        State = "OPEN";
        CreatedAt = now;
    }

    public string AuditEntityType => "flag";

    public Guid Id { get; private set; }
    public string RuleCode { get; private set; } = string.Empty;
    public string Severity { get; private set; } = FlagSeverities.Info;
    public string SubjectType { get; private set; } = string.Empty;
    public Guid SubjectId { get; private set; }
    public string Explanation { get; private set; } = "{}";
    public string State { get; private set; } = "OPEN";
    public DateTimeOffset CreatedAt { get; private set; }
    public int Version { get; private set; }
}
