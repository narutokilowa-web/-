using Mfop.Kernel.Auditing;

namespace Mfop.Sync.Domain;

public static class SyncResults
{
    public const string Applied = "APPLIED", Duplicate = "DUPLICATE", Rejected = "REJECTED";
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
        DateTimeOffset? deviceTime, DateTimeOffset receivedAt, string result, string? resultCode, string? resultMessage, Guid? entityId)
    {
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
