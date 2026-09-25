using Mfop.Access.Contracts;
using Mfop.Audit.Contracts;
using Mfop.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Audit.Api;

public sealed record AuditSearchEntry(
    long Seq, DateTimeOffset OccurredAt, Guid? ActorUserId, string ActorName, IReadOnlyList<string> ActorRoles, Guid? DeviceId, string Action,
    string EntityType, string EntityId, int? EntityVersion, System.Text.Json.JsonElement Changes, System.Text.Json.JsonElement? Facts, string? Reason,
    Guid CommandId, string PrevHash, string Hash);

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        // Auditor: search the tamper-evident audit log by entity (docs/lean-pilot/11 §11.2 M3 item 6).
        app.MapGet("/api/v1/audit/events", async (string? entityType, string? entityId, string? action, int? take, IAuditQueries q, IUserDirectory users, CancellationToken ct) =>
            {
                var events = await q.SearchAsync(entityType, entityId, action, take ?? 200, ct);
                var names = await users.DisplayNamesAsync(events.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value), ct);
                return Results.Ok(events.Select(e => new AuditSearchEntry(
                    e.Seq, e.OccurredAt, e.ActorUserId, e.ActorUserId is { } u ? names.GetValueOrDefault(u, "?") : "SYSTEM", e.ActorRoles, e.DeviceId, e.Action,
                    e.EntityType, e.EntityId, e.EntityVersion, e.Changes, e.Facts, e.Reason, e.CommandId, e.PrevHash, e.Hash)).ToList());
            })
            .WithTags("Audit")
            .RequirePermission("audit.read");
        return app;
    }
}
