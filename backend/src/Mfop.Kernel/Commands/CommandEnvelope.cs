namespace Mfop.Kernel.Commands;

/// <summary>
/// Metadata carried by every state-changing command, from the web (REST) or from devices (sync, M2).
/// <see cref="CommandId"/> becomes the audit event's command_id; it is the idempotency key for device commands (M2).
/// </summary>
public sealed record CommandEnvelope(
    Guid CommandId,
    string Type,
    ActorContext Actor,
    string? Reason = null,
    string? CorrelationId = null)
{
    public static CommandEnvelope For(string type, ActorContext actor, string? reason = null, Guid? commandId = null, string? correlationId = null) =>
        new(commandId ?? Ids.New(), type, actor, reason, correlationId);
}
