using System.Text.Json;

namespace Mfop.Audit.Contracts;

public sealed record AuditEventView(
    long Seq,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string EntityType,
    string EntityId,
    int? EntityVersion,
    JsonElement Changes,
    JsonElement? Facts,
    string? Reason,
    Guid CommandId,
    string Hash);

public interface IAuditQueries
{
    /// <summary>All events for the given entities, ordered by seq.</summary>
    Task<IReadOnlyList<AuditEventView>> ForEntitiesAsync(IReadOnlyCollection<(string EntityType, string EntityId)> entities, CancellationToken ct);
}
