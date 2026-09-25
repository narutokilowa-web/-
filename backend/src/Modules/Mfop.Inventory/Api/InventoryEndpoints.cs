using Mfop.Http;
using Mfop.Inventory.Application;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Domain;
using Mfop.Kernel.Commands;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mfop.Inventory.Api;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/api/v1/stock").WithTags("Inventory");
        stock.MapGet("/items", async (InventoryQueries q, CancellationToken ct) => Results.Ok(await q.ItemsAsync(ct)))
            .RequirePermission("stock.balances.read");
        stock.MapGet("/balances", async (InventoryQueries q, CancellationToken ct) => Results.Ok(await q.BalancesAsync(ct)))
            .RequirePermission("stock.balances.read");
        stock.MapPost("/receipts", async (ReceiptRequest body, HttpContext http, InventoryService s, CancellationToken ct) =>
                (await s.RecordReceiptAsync(http.Command("RecordReceipt", body.Note), body, ct)).ToHttp(ids => Results.Ok(new { movementIds = ids })))
            .RequirePermission("stock.receipt");
        stock.MapPost("/receipts/{movementId:guid}/reverse", async (Guid movementId, ReverseRequest body, HttpContext http, InventoryService s, CancellationToken ct) =>
                (await s.ReverseAsync(http.Command("ReverseReceipt", body.Reason), movementId, MovementTypes.Receipt, body, ct)).ToHttp(id => Results.Ok(new { id })))
            .RequirePermission("stock.receipt");
        stock.MapPost("/issues", async (IssueRequest body, HttpContext http, InventoryService s, CancellationToken ct) =>
                (await s.IssueAsync(http.Command("IssueStock", body.Note), body, ct)).ToHttp(ids => Results.Ok(new { movementIds = ids })))
            .RequirePermission("stock.issue");
        stock.MapPost("/issues/{movementId:guid}/reverse", async (Guid movementId, ReverseRequest body, HttpContext http, InventoryService s, CancellationToken ct) =>
                (await s.ReverseAsync(http.Command("ReverseIssue", body.Reason), movementId, MovementTypes.Issue, body, ct)).ToHttp(id => Results.Ok(new { id })))
            .RequirePermission("stock.issue");

        // Phone: my custody (balances + serials in my hands).
        app.MapGet("/sync/v1/custody", async (CurrentActor actor, InventoryQueries q, CancellationToken ct) => Results.Ok(await q.CustodyOfAsync(actor.Required.UserId!.Value, ct)))
            .WithTags("Sync")
            .RequirePermission("stock.custody.read_own");
        return app;
    }
}
