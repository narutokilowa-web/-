using Mfop.Evidence.Application;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Mfop.Evidence.Api;

public static class EvidenceEndpoints
{
    public static IEndpointRouteBuilder MapEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Phone: stream the photo bytes after AddEvidence was applied (docs/lean-pilot/01 §1.4).
        // With "Content-Range: bytes start-end/total" the photo is sent in chunks and can resume after a dropped connection (M4).
        app.MapPut("/sync/v1/evidence/{id:guid}/content", async (Guid id, HttpRequest request, CurrentActor actor, EvidenceService s, CancellationToken ct) =>
            {
                var range = request.Headers.ContentRange.ToString();
                if (string.IsNullOrEmpty(range)) return (await s.UploadAsync(actor.Required, id, request.Body, request.ContentLength, ct)).ToHttp();
                if (!ContentRangeHeaderValue.TryParse(range, out var parsed) || parsed.From is not { } from || parsed.Length is not { } total)
                {
                    return Problems.From(Kernel.Results.Error.Validation("INVALID_RANGE", "Content-Range must be 'bytes start-end/total'."));
                }

                return (await s.UploadChunkAsync(actor.Required, id, from, total, request.Body, ct)).ToHttp();
            })
            .WithTags("Sync")
            .RequirePermission("workorder.execute");
        app.MapGet("/sync/v1/evidence/{id:guid}/upload", async (Guid id, CurrentActor actor, EvidenceService s, CancellationToken ct) =>
                (await s.UploadStatusAsync(actor.Required, id, ct)).ToHttp())
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
