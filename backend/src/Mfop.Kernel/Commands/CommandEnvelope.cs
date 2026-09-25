namespace Mfop.Kernel.Commands;

/// <summary>
/// Metadata carried by every state-changing command, from the web (REST) or from devices (sync, M2).
/// <see cref="CommandId"/> becomes the audit event's command_id; it is the idempotency key for device commands (M2).
/// <see cref="DeviceTime"/> is the phone clock at capture (recorded as-is); <see cref="EstimatedTime"/> is the server's
/// trusted-time estimate of the capture (M4, docs/lean-pilot/05 §5.7).
/// <see cref="BaseVersion"/> is the version of the target the phone saw when the user acted (for work orders: the last
/// work-order event seq it had pulled). Handlers that overwrite data use it to refuse offline changes made on a stale view
/// (M4: offline work never silently overwrites newer server data).
/// </summary>
public sealed record CommandEnvelope(
    Guid CommandId,
    string Type,
    ActorContext Actor,
    string? Reason = null,
    string? CorrelationId = null,
    DateTimeOffset? DeviceTime = null,
    long? BaseVersion = null,
    DateTimeOffset? EstimatedTime = null)
{
    public static CommandEnvelope For(string type, ActorContext actor, string? reason = null, Guid? commandId = null, string? correlationId = null,
        DateTimeOffset? deviceTime = null, long? baseVersion = null, DateTimeOffset? estimatedTime = null) =>
        new(commandId ?? Ids.New(), type, actor, reason, correlationId, deviceTime, baseVersion, estimatedTime);
}
