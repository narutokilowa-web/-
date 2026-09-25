namespace Mfop.Audit.Domain;

/// <summary>
/// One row of the audit log. Written once by <see cref="Infrastructure.AuditAppender"/>; never updated or deleted
/// (the application role has no UPDATE/DELETE and a trigger rejects them — a guard against bugs, not against a DBA).
/// </summary>
public sealed class AuditEvent
{
    public long Seq { get; init; }
    public Guid EventId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public Guid? ActorUserId { get; init; }
    public string[] ActorRoles { get; init; } = [];
    public Guid? DeviceId { get; init; }
    public string Action { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public int? EntityVersion { get; init; }
    public string Changes { get; init; } = "{}";
    public string? Facts { get; init; }
    public string? Reason { get; init; }
    public Guid CommandId { get; init; }
    public string? CorrelationId { get; init; }
    public string PayloadHash { get; init; } = "";
    public string PrevHash { get; init; } = "";
    public string Hash { get; init; } = "";
}
