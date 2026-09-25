using System.Text.Json.Nodes;

namespace Mfop.Infrastructure.Auditing;

/// <summary>An audit event ready to be appended (seq and hashes are assigned by the appender under a lock).</summary>
public sealed record AuditEventDraft(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    IReadOnlyList<string> ActorRoles,
    Guid? DeviceId,
    string Action,
    string EntityType,
    string EntityId,
    int? EntityVersion,
    JsonObject Changes,
    JsonObject? Facts,
    string? Reason,
    Guid CommandId,
    string? CorrelationId);

/// <summary>Appends events to the hash-chained audit log using the given open transaction. Implemented by the Audit module.</summary>
public interface IAuditAppender
{
    Task AppendAsync(Persistence.DbSession session, IReadOnlyList<AuditEventDraft> events, CancellationToken ct);
}
