using Mfop.Evidence.Application;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Evidence.Api;

public static class EvidenceEndpoints
{
    public static IEndpointRouteBuilder MapEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Phone: stream the photo bytes after AddEvidence was applied (docs/lean-pilot/01 §1.4).
        app.MapPut("/sync/v1/evidence/{id:guid}/content", async (Guid id, HttpRequest request, CurrentActor actor, EvidenceService s, CancellationToken ct) =>
                (await s.UploadAsync(actor.Required, id, request.Body, request.ContentLength, ct)).ToHttp())
            .WithTags("Sync")
            .RequirePermission("workorder.execute");

        var evidence = app.MapGroup("/api/v1/evidence").WithTags("Evidence");
        evidence.MapGet("/{id:guid}/preview", async (Guid id, string? size, CurrentActor actor, EvidenceService s, CancellationToken ct) =>
            {
                var file = await s.DerivedAsync(actor.Required, id, size == "thumb", ct);
                return file.IsSuccess ? Results.Stream(file.Value.Content, file.Value.ContentType) : Problems.From(file.Error!);
            })
            .RequirePermission("evidence.preview");
        evidence.MapGet("/{id:guid}/original", async (Guid id, HttpContext http, EvidenceService s, CancellationToken ct) =>
            {
                var file = await s.OriginalAsync(http.Command("ViewEvidenceOriginal"), id, ct);
                return file.IsSuccess ? Results.Stream(file.Value.Content, file.Value.ContentType, $"evidence-{id:D}.jpg") : Problems.From(file.Error!);
            })
            .RequirePermission("evidence.original");
        return app;
    }
}
