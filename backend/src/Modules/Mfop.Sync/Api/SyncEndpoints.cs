using Mfop.Http;
using Mfop.Kernel.Commands;
using Mfop.Sync.Application;
using Mfop.Sync.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Sync.Api;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var sync = app.MapGroup("/sync/v1").WithTags("Sync");
        sync.MapPost("/push", async (PushRequest body, HttpContext http, CurrentActor actor, SyncService s, CancellationToken ct) =>
                (await s.PushAsync(actor.Required, body, http.TraceIdentifier, ct)).ToHttp())
            .RequirePermission("sync.push")
            .WithMetadata(new DeviceSignatureRequired());
        sync.MapGet("/pull", async (CurrentActor actor, SyncService s, CancellationToken ct) => (await s.PullAsync(actor.Required, ct)).ToHttp())
            .RequirePermission("sync.pull");
        sync.MapGet("/tags/{token}", async (string token, CurrentActor actor, SyncService s, CancellationToken ct) =>
                (await s.AssetCardAsync(actor.Required, token, ct)).ToHttp())
            .RequirePermission("tag.resolve");

        // Supervisor: commands held because their time cannot be proven inside the offline grant (docs/lean-pilot/05 §5.5).
        var quarantine = app.MapGroup("/api/v1/sync/quarantine").WithTags("Sync");
        quarantine.MapGet("/", async (string? state, QuarantineService q, CancellationToken ct) => Results.Ok(await q.ListAsync(state, ct)))
            .RequirePermission("sync.quarantine.read");
        quarantine.MapPost("/{id:guid}/resolve", async (Guid id, ResolveQuarantineRequest body, HttpContext http, QuarantineService q, CancellationToken ct) =>
                (await q.ResolveAsync(http.Command("ResolveQuarantine", body.Reason), id, body, ct)).ToHttp(state => Results.Ok(new { id, state })))
            .RequirePermission("sync.quarantine.resolve");
        return app;
    }
}
