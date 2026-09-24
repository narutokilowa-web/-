using Mfop.Faults.Application;
using Mfop.Faults.Contracts;
using Mfop.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Faults.Api;

public static class FaultEndpoints
{
    public static IEndpointRouteBuilder MapFaultEndpoints(this IEndpointRouteBuilder app)
    {
        var faults = app.MapGroup("/api/v1/faults").WithTags("Faults");
        faults.MapGet("/", async (string? state, Guid? assetId, FaultQueries q, CancellationToken ct) => Results.Ok(await q.ListAsync(state, assetId, ct)))
            .RequirePermission("fault.read");
        faults.MapGet("/{id:guid}", async (Guid id, FaultQueries q, CancellationToken ct) => (await q.GetAsync(id, ct)).ToHttp())
            .RequirePermission("fault.read");
        faults.MapPost("/", async (ReportFaultRequest body, HttpContext http, FaultService s, CancellationToken ct) =>
                (await s.ReportAsync(http.Command("ReportFault", body.Description), body, "WEB", ct))
                .ToHttp(r => r.Result == FaultService.ResultCreated ? Results.Created($"/api/v1/faults/{r.FaultId}", r) : Results.Ok(r)))
            .RequirePermission("fault.report");
        faults.MapPost("/{id:guid}/dismiss", async (Guid id, DismissFaultRequest body, HttpContext http, FaultService s, CancellationToken ct) =>
                (await s.DismissAsync(http.Command("DismissFault", body.Note ?? body.Reason), id, body, ct)).ToHttp(x => Results.Ok(new { id = x })))
            .RequirePermission("fault.dismiss");
        return app;
    }
}
