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
            .RequirePermission("sync.push");
        sync.MapGet("/pull", async (CurrentActor actor, SyncService s, CancellationToken ct) => (await s.PullAsync(actor.Required, ct)).ToHttp())
            .RequirePermission("sync.pull");
        sync.MapGet("/tags/{token}", async (string token, CurrentActor actor, SyncService s, CancellationToken ct) =>
                (await s.AssetCardAsync(actor.Required, token, ct)).ToHttp())
            .RequirePermission("tag.resolve");
        return app;
    }
}
