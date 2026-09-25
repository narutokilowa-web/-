using Mfop.Kernel.Auditing;

namespace Mfop.Sync.Domain;

public static class SyncResults
{
    public const string Applied = "APPLIED", Duplicate = "DUPLICATE", Rejected = "REJECTED", Quarantined = "QUARANTINED";
}

/// <summary>
/// Receipt of one command received through POST /sync/v1/push. Append-only. It makes the command idempotent
/// (the command id is the primary key) and records rejections, which change nothing and therefore have no audit event
/// (docs/lean-pilot/04). An APPLIED receipt is written in the same transaction as the command's changes and audit events.
/// </summary>
public sealed class SyncCommand : IAuditExempt
{
    private SyncCommand() { }

    public SyncCommand(Guid commandId, Guid deviceId, Guid userId, string type, string payload, string payloadHash,
        DateTimeOffset? deviceTime, DateTimeOffset receivedAt, string result, string? resultCode, string? resultMessage, Guid? entityId,
        Guid? grantId = null, DateTimeOffset? estimatedTime = null, string? timeConfidence = null)
    {
        GrantId = grantId;
        EstimatedTime = estimatedTime;
        TimeConfidence = timeConfidence;
        CommandId = commandId;
        DeviceId = deviceId;
        UserId = userId;
        Type = type;
        Payload = payload;
        PayloadHash = payloadHash;
        DeviceTime = deviceTime;
        ReceivedAt = receivedAt;
        Result = result;
        ResultCode = resultCode;
        ResultMessage = resultMessage;
        EntityId = entityId;
    }

    public Guid CommandId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public string PayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset? DeviceTime { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string Result { get; private set; } = SyncResults.Rejected;
    public string? ResultCode { get; private set; }
    public string? ResultMessage { get; private set; }
    public Guid? EntityId { get; private set; }

    /// <summary>M4: the offline grant the command was made under, and the server's trusted-time estimate of its capture.</summary>
    public Guid? GrantId { get; private set; }
    public DateTimeOffset? EstimatedTime { get; private set; }
    public string? TimeConfidence { get; private set; }
}

public static class QuarantineStates
{
    public const string Pending = "PENDING", Applied = "APPLIED", Discarded = "DISCARDED", ApplyFailed = "APPLY_FAILED";
}

public static class QuarantineCommands
{
    public const string QuarantineCommand = nameof(QuarantineCommand);
    public const string ApplyQuarantined = nameof(ApplyQuarantined);
    public const string DiscardQuarantined = nameof(DiscardQuarantined);
    public const string RecordApplyFailure = nameof(RecordApplyFailure);
}

/// <summary>Transition table. MUST equal docs/state-machines/sync-quarantine.yaml (StateMachineConformanceTests).</summary>
public static class SyncStateMachines
{
    public static readonly Mfop.Kernel.StateMachines.StateMachine Quarantine = new(
        "SyncQuarantine",
        [QuarantineStates.Pending, QuarantineStates.Applied, QuarantineStates.Discarded, QuarantineStates.ApplyFailed],
        QuarantineStates.Pending,
        [QuarantineStates.Applied, QuarantineStates.Discarded, QuarantineStates.ApplyFailed],
        [
            new("Q1", [Mfop.Kernel.StateMachines.StateMachine.Start], QuarantineCommands.QuarantineCommand, QuarantineStates.Pending, ["system"]),
            new("Q2", [QuarantineStates.Pending], QuarantineCommands.ApplyQuarantined, QuarantineStates.Applied, ["SUPERVISOR"]),
            new("Q3", [QuarantineStates.Pending], QuarantineCommands.DiscardQuarantined, QuarantineStates.Discarded, ["SUPERVISOR"]),
            new("Q4", [QuarantineStates.Pending], QuarantineCommands.RecordApplyFailure, QuarantineStates.ApplyFailed, ["SUPERVISOR"]),
        ]);
}

/// <summary>
/// A pushed command whose capture time cannot be proven inside its offline grant (docs/lean-pilot/05 §5.5). Nothing was
/// applied; a supervisor (not the command's author) decides: apply it now as the technician, or discard it with a reason.
/// </summary>
public sealed class QuarantinedCommand : IAuditable
{
    private QuarantinedCommand() { }

    public QuarantinedCommand(Guid id, Guid commandId, string type, string payload, Guid userId, string[] userRoles, Guid deviceId,
        string reasonCode, string reason, DateTimeOffset? deviceTime, DateTimeOffset estimatedTime, string timeConfidence,
        DateTimeOffset receivedAt, Guid? grantId, long? baseVersion)
    {
        Id = id;
        CommandId = commandId;
        Type = type;
        Payload = payload;
        UserId = userId;
        UserRoles = userRoles;
        DeviceId = deviceId;
        ReasonCode = reasonCode;
        Reason = reason;
        DeviceTime = deviceTime;
        EstimatedTime = estimatedTime;
        TimeConfidence = timeConfidence;
        ReceivedAt = receivedAt;
        GrantId = grantId;
        BaseVersion = baseVersion;
        State = SyncStateMachines.Quarantine.Create(QuarantineCommands.QuarantineCommand).Value.To;
    }

    public string AuditEntityType => "sync_quarantine";

    public Guid Id { get; private set; }
    public Guid CommandId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Payload { get; private set; } = "{}";
    public Guid UserId { get; private set; }

    /// <summary>The author's roles when the command was received (its permission was checked then).</summary>
    public string[] UserRoles { get; private set; } = [];
    public Guid DeviceId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset? DeviceTime { get; private set; }
    public DateTimeOffset EstimatedTime { get; private set; }
    public string TimeConfidence { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; private set; }
    public Guid? GrantId { get; private set; }
    public long? BaseVersion { get; private set; }
    public string State { get; private set; } = QuarantineStates.Pending;
    public Guid? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolutionReason { get; private set; }
    public string? ResultCode { get; private set; }
    public Guid? EntityId { get; private set; }
    public int Version { get; private set; }

    public Mfop.Kernel.Results.Result<Mfop.Kernel.StateMachines.Transition> Resolve(
        string command, Guid resolverId, string? reason, DateTimeOffset now, string? resultCode = null, Guid? entityId = null)
    {
        var t = SyncStateMachines.Quarantine.Resolve(State, command);
        if (!t.IsSuccess) return t;
        if (resolverId == UserId) return Mfop.Kernel.Results.Error.Sod("The author of a quarantined command cannot decide on it.");
        if (string.IsNullOrWhiteSpace(reason)) return Mfop.Kernel.Results.Error.Validation("REASON_REQUIRED", "A reason is required.");
        State = t.Value.To;
        ResolvedBy = resolverId;
        ResolvedAt = now;
        ResolutionReason = reason.Trim();
        ResultCode = resultCode;
        EntityId = entityId;
        return t;
    }
}

/// <summary>Last sync activity of a device (telemetry for the Devices page and offboarding). Not business state.</summary>
public sealed class DeviceSyncState : IAuditExempt
{
    private DeviceSyncState() { }

    public DeviceSyncState(Guid deviceId, Guid userId)
    {
        DeviceId = deviceId;
        UserId = userId;
    }

    public Guid DeviceId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset? LastPullAt { get; private set; }
    public DateTimeOffset? LastPushAt { get; private set; }
    public int? LastReportedPendingCommands { get; private set; }

    public void Pulled(DateTimeOffset at) => LastPullAt = at;

    public void Pushed(DateTimeOffset at, int? pendingCommands)
    {
        LastPushAt = at;
        LastReportedPendingCommands = pendingCommands;
    }
}
