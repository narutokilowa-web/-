using System.Text.Json;

namespace Mfop.Audit.Contracts;

public sealed record AuditEventView(
    long Seq,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    IReadOnlyList<string> ActorRoles,
    Guid? DeviceId,
    string Action,
    string EntityType,
    string EntityId,
    int? EntityVersion,
    JsonElement Changes,
    JsonElement? Facts,
    string? Reason,
    Guid CommandId,
    string PrevHash,
    string Hash);

public interface IAuditQueries
{
    /// <summary>All events for the given entities, ordered by seq.</summary>
    Task<IReadOnlyList<AuditEventView>> ForEntitiesAsync(IReadOnlyCollection<(string EntityType, string EntityId)> entities, CancellationToken ct);

    /// <summary>Auditor search (newest first, at most <paramref name="take"/>).</summary>
    Task<IReadOnlyList<AuditEventView>> SearchAsync(string? entityType, string? entityId, string? action, int take, CancellationToken ct);
}
