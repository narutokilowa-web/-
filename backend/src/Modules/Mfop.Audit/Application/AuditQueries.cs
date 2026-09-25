using System.Text.Json;
using Mfop.Audit.Contracts;
using Mfop.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Audit.Application;

public sealed class AuditQueries(AuditDbContext db) : IAuditQueries
{
    public async Task<IReadOnlyList<AuditEventView>> ForEntitiesAsync(IReadOnlyCollection<(string EntityType, string EntityId)> entities, CancellationToken ct)
    {
        if (entities.Count == 0)
        {
            return [];
        }

        var keys = entities.Select(e => e.EntityType + "|" + e.EntityId).ToList();
        var rows = await db.Events.AsNoTracking()
            .Where(e => keys.Contains(e.EntityType + "|" + e.EntityId))
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);

        return rows.Select(ToView).ToList();
    }

    public async Task<IReadOnlyList<AuditEventView>> SearchAsync(string? entityType, string? entityId, string? action, int take, CancellationToken ct)
    {
        var query = db.Events.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(e => e.EntityType == entityType.Trim());
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(e => e.EntityId == entityId.Trim());
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(e => e.Action.StartsWith(action.Trim()));
        var rows = await query.OrderByDescending(e => e.Seq).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        return rows.Select(ToView).ToList();
    }

    private static AuditEventView ToView(Domain.AuditEvent r) => new(
        r.Seq, r.OccurredAt, r.ActorUserId, r.ActorRoles, r.DeviceId, r.Action, r.EntityType, r.EntityId, r.EntityVersion,
        JsonDocument.Parse(r.Changes).RootElement.Clone(),
        r.Facts is null ? null : JsonDocument.Parse(r.Facts).RootElement.Clone(),
        r.Reason, r.CommandId, r.PrevHash, r.Hash);
}
