namespace Mfop.Rules.Contracts;

/// <summary>A RESOLVED fault of the asset with the diagnosis of the work order that resolved it (input of the rule).</summary>
public sealed record ResolvedFailure(Guid FaultId, Guid WorkOrderId, string WorkOrderCode, string DiagnosisCode, bool CountsAsFailure, DateTimeOffset ResolvedAt);

public sealed record CountedFailure(Guid FaultId, Guid WorkOrderId, string WorkOrderCode, string DiagnosisCode, DateTimeOffset ResolvedAt);

/// <param name="Code">FULL_INSPECTION.</param>
public sealed record RecommendationDto(
    Guid Id, Guid AssetId, string Code, string RuleCode, string State, int Count, int Threshold, int WindowDays,
    IReadOnlyList<CountedFailure> CountedFailures, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record RepeatedFailureEvaluation(bool Fires, int Count, int Threshold, int WindowDays, Guid? RecommendationId);

/// <summary>Evaluated by the WorkOrders module when a fault becomes RESOLVED, inside that unit of work (audited).</summary>
public interface IRepeatedFailureRule
{
    Task<RepeatedFailureEvaluation> EvaluateAsync(Guid assetId, IReadOnlyList<ResolvedFailure> resolved, CancellationToken ct);
}

public interface IRecommendations
{
    Task<IReadOnlyList<RecommendationDto>> OpenForAssetAsync(Guid assetId, CancellationToken ct);
}

public sealed record FlagDto(Guid Id, string RuleCode, string Severity, string SubjectType, Guid SubjectId, string State, System.Text.Json.JsonElement Explanation, DateTimeOffset CreatedAt);

/// <summary>Integrity flags raised inside the caller's unit of work (audited). Flags are signals for review, never verdicts.</summary>
public interface IFlags
{
    /// <param name="explanation">Values must be strings, booleans, integers or null.</param>
    Task<Guid> RaiseAsync(string ruleCode, string severity, string subjectType, Guid subjectId, IReadOnlyDictionary<string, object?> explanation, CancellationToken ct);

    Task<IReadOnlyList<FlagDto>> ForSubjectsAsync(string subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct);
}

public static class FlagCodes
{
    public const string EvidenceHashMismatch = "EVIDENCE_HASH_MISMATCH";
    public const string EvidenceDuplicate = "EVIDENCE_DUPLICATE";
    public const string ConsumptionExceedsCustody = "CONSUMPTION_EXCEEDS_CUSTODY";
    public const string SerialNotInCustody = "SERIAL_NOT_IN_CUSTODY";

    // M4 (docs/lean-pilot/05 §5.5–5.7, §5.6)
    public const string GrantInvalid = "GRANT_INVALID";
    public const string GrantExpired = "GRANT_EXPIRED";
    public const string ClockSkew = "CLOCK_SKEW";
    public const string NeedsReassignment = "NEEDS_REASSIGNMENT";
    public const string CustodyToReconcile = "CUSTODY_TO_RECONCILE";
    public const string SubmittedByNonAssignee = "SUBMITTED_BY_NON_ASSIGNEE"; // docs/spec/10 §10.5: field facts from the previous assignee
}

public sealed record RuleSettingDto(string RuleCode, bool Enabled, System.Text.Json.JsonElement Params, Guid? ChangedBy, DateTimeOffset ChangedAt, int Version);

public sealed record ChangeRepeatedFailureSettingRequest(int Threshold, int WindowDays, bool Enabled, string? Reason);
