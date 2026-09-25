using Mfop.Assets.Application;
using Mfop.Assets.Contracts;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Assets.Api;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var reg = app.MapGroup("/api/v1/registrations").WithTags("Registrations");

        reg.MapGet("/", async (string? state, CurrentActor actor, AssetQueries q, CancellationToken ct) =>
                Results.Ok(await q.ListRegistrationsAsync(actor.Required, state, ct)))
            .RequirePermission("registration.read");
        reg.MapGet("/{id:guid}", async (Guid id, CurrentActor actor, AssetQueries q, CancellationToken ct) =>
                (await q.GetRegistrationAsync(actor.Required, id, ct)).ToHttp())
            .RequirePermission("registration.read");

        reg.MapPost("/", async (RegistrationRequest body, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.DraftAsync(http.Command("RegisterAssetDraft"), body, ct)).ToHttp(id => Results.Created($"/api/v1/registrations/{id}", new { id })))
            .RequirePermission("registration.draft");
        reg.MapPost("/{id:guid}/update", async (Guid id, RegistrationRequest body, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.UpdateDraftAsync(http.Command("UpdateRegistrationDraft"), id, body, ct)).ToHttp(Ok))
            .RequirePermission("registration.draft");
        reg.MapPost("/{id:guid}/submit", async (Guid id, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.SubmitAsync(http.Command("SubmitRegistration"), id, ct)).ToHttp(Ok))
            .RequirePermission("registration.submit");
        reg.MapPost("/{id:guid}/discard", async (Guid id, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.DiscardAsync(http.Command("DiscardDraft"), id, ct)).ToHttp(Ok))
            .RequirePermission("registration.discard");
        reg.MapPost("/{id:guid}/approve", async (Guid id, DecisionRequest body, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.ApproveAsync(http.Command("ApproveRegistration", body.Note), id, body.Note, ct)).ToHttp(Ok))
            .RequirePermission("registration.decide");
        reg.MapPost("/{id:guid}/return", async (Guid id, DecisionRequest body, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.ReturnAsync(http.Command("ReturnRegistration", body.Note), id, body.Note, ct)).ToHttp(Ok))
            .RequirePermission("registration.decide");
        reg.MapPost("/{id:guid}/reject", async (Guid id, DecisionRequest body, HttpContext http, RegistrationCommands c, CancellationToken ct) =>
                (await c.RejectAsync(http.Command("RejectRegistration", body.Note), id, body.Note, ct)).ToHttp(Ok))
            .RequirePermission("registration.decide");

        var assets = app.MapGroup("/api/v1/assets").WithTags("Assets");
        assets.MapGet("/", async (string? q, AssetQueries queries, CancellationToken ct) => Results.Ok(await queries.ListAssetsAsync(q, ct)))
            .RequirePermission("asset.read");
        assets.MapGet("/{id:guid}", async (Guid id, AssetQueries queries, CancellationToken ct) => (await queries.GetAssetAsync(id, ct)).ToHttp())
            .RequirePermission("asset.read");
        assets.MapGet("/{id:guid}/history", async (Guid id, AssetQueries queries, CancellationToken ct) => (await queries.HistoryAsync(id, ct)).ToHttp())
            .RequirePermission("asset.history.read");

        app.MapGet("/api/v1/tags/{token}", async (string token, AssetQueries queries, CancellationToken ct) =>
                (await queries.ResolveTagAsync(token, ct)).ToHttp(assetId => Results.Ok(new { assetId })))
            .WithTags("Assets")
            .RequirePermission("tag.resolve");

        return app;
    }

    private static IResult Ok(Guid id) => Results.Ok(new { id });
}
