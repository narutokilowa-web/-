using Mfop.Http;
using Mfop.Inventory.Application;
using Mfop.Inventory.Contracts;
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
                (await s.ReverseReceiptAsync(http.Command("ReverseReceipt", body.Reason), movementId, body, ct)).ToHttp(id => Results.Ok(new { id })))
            .RequirePermission("stock.receipt");

        // M5 (04 §4.9): issue → IN_TRANSIT; the technician acknowledges or disputes on the phone; the warehouse cancels.
        stock.MapGet("/issues", async (string? state, Guid? technicianId, InventoryQueries q, CancellationToken ct) => Results.Ok(await q.IssuesAsync(state, technicianId, ct)))
            .RequirePermission("stock.balances.read");
        stock.MapGet("/technicians", async (Mfop.Access.Contracts.IAssigneeDirectory d, CancellationToken ct) => Results.Ok(await d.TechniciansAsync(ct)))
            .RequirePermission("stock.issue");
        stock.MapPost("/issues", async (IssueRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.IssueAsync(http.Command("IssueStock", body.Note), body, ct)).ToHttp(r => Results.Ok(r)))
            .RequirePermission("stock.issue");
        stock.MapPost("/issues/{id:guid}/cancel", async (Guid id, ReverseRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.CancelIssueAsync(http.Command("CancelStockIssue", body.Reason), id, body, ct)).ToHttp(x => Results.Ok(new { id = x })))
            .RequirePermission("stock.issue");

        // M5 (04 §4.10): unused returns declared on the phone, received or rejected here; offboarding returns.
        stock.MapGet("/returns", async (string? state, Guid? technicianId, InventoryQueries q, CancellationToken ct) => Results.Ok(await q.ReturnsAsync(state, technicianId, ct)))
            .RequirePermission("stock.balances.read");
        stock.MapPost("/returns/{id:guid}/receive", async (Guid id, ReceiveReturnRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.ReceiveReturnAsync(http.Command("ReceiveStockReturn", body.Note), id, body, ct)).ToHttp(x => Results.Ok(new { id = x })))
            .RequirePermission("stock.return.receive");
        stock.MapPost("/returns/{id:guid}/reject", async (Guid id, ReverseRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.RejectReturnAsync(http.Command("RejectStockReturn", body.Reason), id, body, ct)).ToHttp(x => Results.Ok(new { id = x })))
            .RequirePermission("stock.return.receive");
        stock.MapPost("/offboarding-returns", async (OffboardingReturnRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.OffboardingReturnAsync(http.Command("ReceiveOffboardingReturn", body.Note), body, ct)).ToHttp(r => Results.Ok(r)))
            .RequirePermission("stock.return.receive");

        // M5 (04 §4.11): failed parts.
        stock.MapGet("/core-returns", async (string? state, Guid? technicianId, InventoryQueries q, CancellationToken ct) =>
                Results.Ok(await q.CoreReturnsAsync(state, technicianId, ct)))
            .RequirePermission("stock.balances.read");
        stock.MapPost("/core-returns/{id:guid}/receive", async (Guid id, ReceiveCoreRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.ReceiveCoreAsync(http.Command("ReceiveCorePart", body.Reason), id, body, ct)).ToHttp(x => Results.Ok(new { id = x })))
            .RequirePermission("stock.return.receive");
        stock.MapPost("/core-returns/{id:guid}/waive", async (Guid id, ReverseRequest body, HttpContext http, StockDocumentService s, CancellationToken ct) =>
                (await s.WaiveCoreAsync(http.Command("WaiveCoreReturn", body.Reason), id, body, ct)).ToHttp(x => Results.Ok(new { id = x })))
            .RequirePermission("core_return.waive");

        // M5: reconciliation and stock history (read-only; findings are signals, stock only changes through its commands).
        stock.MapGet("/reconciliation", async (InventoryQueries q, CancellationToken ct) => Results.Ok(await q.ReconciliationAsync(ct)))
            .RequirePermission("stock.balances.read");
        stock.MapGet("/movements", async (Guid? itemId, Guid? locationId, Guid? technicianId, string? serial, string? type, Guid? documentId, InventoryQueries q,
                    CancellationToken ct) => Results.Ok(await q.HistoryAsync(itemId, locationId, technicianId, serial, type, documentId, ct)))
            .RequirePermission("stock.balances.read");

        // Phone: my custody (balances + serials in my hands).
        app.MapGet("/sync/v1/custody", async (CurrentActor actor, InventoryQueries q, CancellationToken ct) => Results.Ok(await q.CustodyOfAsync(actor.Required.UserId!.Value, ct)))
            .WithTags("Sync")
            .RequirePermission("stock.custody.read_own");
        return app;
    }
}
