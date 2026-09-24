using Mfop.Access.Contracts;
using Mfop.Http;
using Mfop.Kernel.Commands;
using Mfop.WorkOrders.Application;
using Mfop.WorkOrders.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.WorkOrders.Api;

public static class WorkOrderEndpoints
{
    public static IEndpointRouteBuilder MapWorkOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var wo = app.MapGroup("/api/v1/work-orders").WithTags("WorkOrders");
        wo.MapGet("/", async (string? state, WorkOrderQueries q, CancellationToken ct) => Results.Ok(await q.ListAsync(state, ct)))
            .RequirePermission("workorder.read");
        wo.MapGet("/{id:guid}", async (Guid id, CurrentActor actor, WorkOrderQueries q, CancellationToken ct) => (await q.GetAsync(id, actor.Required, ct)).ToHttp())
            .RequirePermission("workorder.read");
        wo.MapGet("/{id:guid}/timeline", async (Guid id, WorkOrderQueries q, CancellationToken ct) => (await q.TimelineAsync(id, ct)).ToHttp())
            .RequirePermission("workorder.read");
        wo.MapPost("/", async (CreateWorkOrderRequest body, HttpContext http, WorkOrderService s, CancellationToken ct) =>
                (await s.CreateAsync(http.Command("CreateWorkOrder", body.Note), body, ct)).ToHttp(id => Results.Created($"/api/v1/work-orders/{id}", new { id })))
            .RequirePermission("workorder.create");
        wo.MapPost("/{id:guid}/assign", async (Guid id, AssignRequest body, HttpContext http, WorkOrderService s, CancellationToken ct) =>
                (await s.AssignAsync(http.Command("Assign", body.Reason), id, body, ct)).ToHttp(Ok))
            .RequirePermission("workorder.assign");
        wo.MapPost("/{id:guid}/reassign", async (Guid id, AssignRequest body, HttpContext http, WorkOrderService s, CancellationToken ct) =>
                (await s.ReassignAsync(http.Command("Reassign", body.Reason), id, body, ct)).ToHttp(Ok))
            .RequirePermission("workorder.assign");
        wo.MapPost("/{id:guid}/cancel", async (Guid id, CancelRequest body, HttpContext http, WorkOrderService s, CancellationToken ct) =>
                (await s.CancelAsync(http.Command("Cancel", body.Reason), id, body, ct)).ToHttp(Ok))
            .RequirePermission("workorder.cancel");

        app.MapGet("/api/v1/technicians", async (IAssigneeDirectory d, CancellationToken ct) => Results.Ok(await d.TechniciansAsync(ct)))
            .WithTags("WorkOrders")
            .RequirePermission("workorder.assign");

        // Phone: open one task (fresh from the server). Only the current assignee (N5).
        app.MapGet("/sync/v1/work-orders/{id:guid}", async (Guid id, CurrentActor actor, WorkOrderQueries q, CancellationToken ct) =>
                (await q.TaskForAssigneeAsync(id, actor.Required, ct)).ToHttp())
            .WithTags("Sync")
            .RequirePermission("workorder.read_assigned");
        return app;
    }

    private static IResult Ok(Guid id) => Results.Ok(new { id });
}
