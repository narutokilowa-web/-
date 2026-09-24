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

        return rows.Select(r => new AuditEventView(
            r.Seq, r.OccurredAt, r.ActorUserId, r.ActorRoles, r.Action, r.EntityType, r.EntityId, r.EntityVersion,
            JsonDocument.Parse(r.Changes).RootElement.Clone(),
            r.Facts is null ? null : JsonDocument.Parse(r.Facts).RootElement.Clone(),
            r.Reason, r.CommandId, r.Hash)).ToList();
    }
}
