using System.Globalization;
using Mfop.Access.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Domain;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Inventory.Application;

/// <summary>
/// Minimal inventory for DEMO 1 (docs/lean-pilot/07): RECEIPT, one-step ISSUE to a technician's custody, CONSUME on a
/// work order, and REVERSAL. Every movement is an append-only ledger row; balances are updated in the same transaction
/// and the verifier recomputes them from the movements.
/// </summary>
public sealed class InventoryService(InventoryDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IAssigneeDirectory assignees)
{
    /// <summary>Master data (demo seed only; there is no item-management UI in DEMO 1).</summary>
    public Task<Result<Guid>> DefineItemAsync(CommandEnvelope cmd, string sku, string nameAr, string nameEn, string unit, bool serialTracked,
        bool coreReturnRequired, decimal? costAmount, string? costCurrency, DateOnly? costAsOf, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (unit is not ("EA" or "M")) return Error.Validation("INVALID_UNIT", "Unit must be EA or M.");
            var existing = await db.Items.SingleOrDefaultAsync(i => i.Sku == sku, innerCt);
            if (existing is not null) return existing.Id;
            var item = new Item(Ids.New(), sku, nameAr, nameEn, unit, serialTracked, coreReturnRequired);
            db.Items.Add(item);
            audit.Record(item, "item.defined");
            if (costAmount is { } amount && costCurrency is { } currency && costAsOf is { } asOf)
            {
                var money = new Money(amount, currency, asOf); // validates the ISO code; no default currency is assumed here
                var cost = new ItemCost(Ids.New(), item.Id, money.Amount, money.Currency, money.AsOf, cmd.Actor.UserId, clock.UtcNowMicros());
                db.ItemCosts.Add(cost);
                audit.Record(cost, "item_cost.set");
            }

            return item.Id;
        }, ct);

    /// <summary>S1: opening balance / receipt from the supplier into the warehouse.</summary>
    public Task<Result<IReadOnlyList<Guid>>> RecordReceiptAsync(CommandEnvelope cmd, ReceiptRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<IReadOnlyList<Guid>>(cmd, async innerCt =>
        {
            if (request.Lines is not { Count: > 0 }) return Error.Validation("LINES_REQUIRED", "At least one line is required.");
            var supplier = await EnsureLocationAsync(LocationKinds.Supplier, null, innerCt);
            var warehouse = await EnsureLocationAsync(LocationKinds.Warehouse, null, innerCt);
            var ids = new List<Guid>();
            foreach (var line in request.Lines)
            {
                var item = await db.Items.SingleOrDefaultAsync(i => i.Id == line.ItemId, innerCt);
                if (item is null) return Error.NotFound("ITEM_NOT_FOUND", "Item not found.");
                var (quantity, serials, error) = ValidateLine(item, line);
                if (error is not null) return error;
                if (item.SerialTracked)
                {
                    var located = await SerialLocationsAsync(item.Id, innerCt);
                    var known = serials.Where(s => located.TryGetValue(s, out var loc) && loc != supplier.Id).ToList();
                    if (known.Count > 0) return Error.Validation("SERIAL_ALREADY_IN_STOCK", $"Serial(s) already in stock: {string.Join(", ", known)}.");
                }

                ids.Add(await MoveAsync(cmd, MovementTypes.Receipt, item, quantity, supplier, warehouse, serials, null, null, null, clock.UtcNowMicros(), request.Note, innerCt));
            }

            return ids;
        }, ct);

    /// <summary>S2: warehouse → technician custody in one step (acknowledgment is M5). Issuer ≠ receiver; never below zero (N14).</summary>
    public Task<Result<IReadOnlyList<Guid>>> IssueAsync(CommandEnvelope cmd, IssueRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<IReadOnlyList<Guid>>(cmd, async innerCt =>
        {
            if (request.Lines is not { Count: > 0 }) return Error.Validation("LINES_REQUIRED", "At least one line is required.");
            if (request.TechnicianId == cmd.Actor.UserId) return Error.Sod("The warehouse officer issuing stock cannot be the receiving technician.");
            var check = await assignees.CheckAsync(request.TechnicianId, innerCt);
            if (!check.Exists) return Error.NotFound("TECHNICIAN_NOT_FOUND", "User not found.");
            if (!check.IsActive || !check.IsTechnician) return Error.Validation("RECEIVER_NOT_TECHNICIAN", "Stock can only be issued to an active technician.");

            var warehouse = await EnsureLocationAsync(LocationKinds.Warehouse, null, innerCt);
            var custody = await EnsureLocationAsync(LocationKinds.Custody, request.TechnicianId, innerCt);
            var ids = new List<Guid>();
            foreach (var line in request.Lines)
            {
                var item = await db.Items.SingleOrDefaultAsync(i => i.Id == line.ItemId, innerCt);
                if (item is null) return Error.NotFound("ITEM_NOT_FOUND", "Item not found.");
                var (quantity, serials, error) = ValidateLine(item, line);
                if (error is not null) return error;
                var available = await BalanceAsync(warehouse.Id, item.Id, innerCt);
                if (available < quantity)
                {
                    return Error.Validation("INSUFFICIENT_STOCK", $"The warehouse holds {available.ToString("0.###", CultureInfo.InvariantCulture)} {item.Unit} of {item.Sku}.");
                }

                if (item.SerialTracked)
                {
                    var located = await SerialLocationsAsync(item.Id, innerCt);
                    var missing = serials.Where(s => !located.TryGetValue(s, out var loc) || loc != warehouse.Id).ToList();
                    if (missing.Count > 0) return Error.Validation("SERIAL_NOT_IN_WAREHOUSE", $"Serial(s) not in the warehouse: {string.Join(", ", missing)}.");
                }

                ids.Add(await MoveAsync(cmd, MovementTypes.Issue, item, quantity, warehouse, custody, serials, null, null, null, clock.UtcNowMicros(), request.Note, innerCt));
            }

            return ids;
        }, ct);

    /// <summary>S4 (receipt) / S5 (issue): a REVERSAL movement in the opposite direction. The original stays as it is.</summary>
    public Task<Result<Guid>> ReverseAsync(CommandEnvelope cmd, Guid movementId, string expectedType, ReverseRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason)) return Error.Validation("REASON_REQUIRED", "A reason is required for a reversal.");
            var original = await db.Movements.SingleOrDefaultAsync(m => m.Id == movementId, innerCt);
            if (original is null || original.Type != expectedType) return Error.NotFound("MOVEMENT_NOT_FOUND", "Movement not found.");
            if (await db.Movements.AnyAsync(m => m.ReversalOf == movementId, innerCt)) return Error.Conflict("ALREADY_REVERSED", "This movement was already reversed.");
            var command = expectedType == MovementTypes.Receipt ? LedgerCommands.ReverseReceipt : LedgerCommands.ReverseIssue;
            var from = await db.Locations.SingleAsync(l => l.Id == original.ToLocationId, innerCt);
            var to = await db.Locations.SingleAsync(l => l.Id == original.FromLocationId, innerCt);
            var t = InventoryStateMachines.StockLedger.Resolve(from.Kind, command);
            if (!t.IsSuccess) return t.Error!;
            if (await BalanceAsync(from.Id, original.ItemId, innerCt) < original.Quantity)
            {
                return Error.Validation("INSUFFICIENT_STOCK", "The quantity is no longer at the location; it cannot be reversed.");
            }

            var item = await db.Items.SingleAsync(i => i.Id == original.ItemId, innerCt);
            return await MoveAsync(cmd, MovementTypes.Reversal, item, original.Quantity, from, to, original.Serials, null, null, original.Id,
                clock.UtcNowMicros(), request.Reason.Trim(), innerCt);
        }, ct);

    /// <summary>S3 inside the caller's unit of work. Consuming more than the known custody is applied and marked (07 §7.2).</summary>
    internal async Task<Result<ConsumeOutcome>> ConsumeInCurrentUnitOfWorkAsync(ConsumeRequest request, CancellationToken ct)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == request.ItemId, ct);
        if (item is null) return Error.NotFound("ITEM_NOT_FOUND", "Item not found.");
        var (quantity, serials, error) = ValidateLine(item, new StockLine(item.Id, request.Quantity, request.Serials));
        if (error is not null) return error;
        if (await db.Movements.AnyAsync(m => m.Id == request.MovementId, ct)) return Error.Conflict("MOVEMENT_EXISTS", "This consumption id was already used.");

        var custody = await EnsureLocationAsync(LocationKinds.Custody, request.TechnicianId, ct);
        var consumed = await EnsureLocationAsync(LocationKinds.Consumed, null, ct);
        var before = await BalanceAsync(custody.Id, item.Id, ct);
        var exceeds = quantity > before;
        var serialNotInCustody = false;
        if (item.SerialTracked)
        {
            var located = await SerialLocationsAsync(item.Id, ct);
            serialNotInCustody = serials.Any(s => !located.TryGetValue(s, out var loc) || loc != custody.Id);
        }

        var cmd = CommandEnvelope.For(LedgerCommands.ConsumeMaterial, new ActorContext(request.TechnicianId, []), null, request.CommandId);
        var id = await MoveAsync(cmd, MovementTypes.Consume, item, quantity, custody, consumed, serials, request.WorkOrderId, request.AssetId, null,
            ClockExtensions.TruncateToMicros(request.OccurredAt), null, ct, request.MovementId, exceeds, serialNotInCustody);
        return new ConsumeOutcome(id, exceeds, serialNotInCustody, before);
    }

    private async Task<Guid> MoveAsync(CommandEnvelope cmd, string type, Item item, decimal quantity, StockLocation from, StockLocation to, string[] serials,
        Guid? workOrderId, Guid? assetId, Guid? reversalOf, DateTimeOffset occurredAt, string? note, CancellationToken ct,
        Guid? id = null, bool exceedsCustody = false, bool serialNotInCustody = false)
    {
        var movement = new StockMovement(id ?? Ids.New(), type, item.Id, quantity, from.Id, to.Id, serials, workOrderId, assetId, reversalOf,
            cmd.Actor.UserId!.Value, cmd.CommandId, occurredAt, clock.UtcNowMicros(), string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            exceedsCustody, serialNotInCustody);
        db.Movements.Add(movement);
        audit.Record(movement, $"stock_movement.{type.ToLowerInvariant()}", extra: new Dictionary<string, object?>
        {
            ["sku"] = item.Sku,
            ["quantity"] = quantity.ToString("0.000", CultureInfo.InvariantCulture),
            ["from_kind"] = from.Kind,
            ["to_kind"] = to.Kind,
        });
        await AdjustAsync(from.Id, item.Id, -quantity, ct);
        await AdjustAsync(to.Id, item.Id, quantity, ct);
        return movement.Id;
    }

    private async Task AdjustAsync(Guid locationId, Guid itemId, decimal delta, CancellationToken ct)
    {
        var balance = db.Balances.Local.SingleOrDefault(b => b.LocationId == locationId && b.ItemId == itemId)
                      ?? await db.Balances.SingleOrDefaultAsync(b => b.LocationId == locationId && b.ItemId == itemId, ct);
        if (balance is null)
        {
            balance = new StockBalance(Ids.New(), locationId, itemId);
            db.Balances.Add(balance);
        }

        balance.Add(delta);
        audit.Record(balance, "stock_balance.changed");
    }

    private async Task<decimal> BalanceAsync(Guid locationId, Guid itemId, CancellationToken ct) =>
        (db.Balances.Local.SingleOrDefault(b => b.LocationId == locationId && b.ItemId == itemId)
         ?? await db.Balances.AsNoTracking().SingleOrDefaultAsync(b => b.LocationId == locationId && b.ItemId == itemId, ct))?.Quantity ?? 0m;

    private async Task<StockLocation> EnsureLocationAsync(string kind, Guid? custodian, CancellationToken ct)
    {
        var location = db.Locations.Local.SingleOrDefault(l => l.Kind == kind && l.CustodianUserId == custodian)
                       ?? await db.Locations.SingleOrDefaultAsync(l => l.Kind == kind && l.CustodianUserId == custodian, ct);
        if (location is not null) return location;
        location = new StockLocation(Ids.New(), kind, kind switch
        {
            LocationKinds.Warehouse => "المستودع",
            LocationKinds.Supplier => "المورّد",
            LocationKinds.Consumed => "مستهلك في أوامر العمل",
            _ => "عهدة فني",
        }, custodian);
        db.Locations.Add(location);
        audit.Record(location, "stock_location.created");
        return location;
    }

    /// <summary>Where each serial of an item is now: the destination of the latest movement that carried it.</summary>
    private async Task<Dictionary<string, Guid>> SerialLocationsAsync(Guid itemId, CancellationToken ct)
    {
        var movements = await db.Movements.AsNoTracking().Where(m => m.ItemId == itemId && m.Serials.Length > 0)
            .OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var located = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var m in movements.Concat(db.Movements.Local.Where(m => m.ItemId == itemId)))
        {
            foreach (var s in m.Serials)
            {
                located[s] = m.ToLocationId;
            }
        }

        return located;
    }

    private static (decimal Quantity, string[] Serials, Error? Error) ValidateLine(Item item, StockLine line)
    {
        var quantity = Quantities.Normalize(line.Quantity);
        if (quantity <= 0 || quantity > 100000) return (0, [], Error.Validation("INVALID_QUANTITY", "The quantity must be positive."));
        if (item.Unit == "EA" && quantity != decimal.Truncate(quantity)) return (0, [], Error.Validation("INVALID_QUANTITY", "This item is counted in whole units."));
        var serials = (line.Serials ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        if (serials.Any(s => s.Length > 60)) return (0, [], Error.Validation("INVALID_SERIAL", "A serial is at most 60 characters."));
        if (serials.Distinct(StringComparer.Ordinal).Count() != serials.Length) return (0, [], Error.Validation("DUPLICATE_SERIAL", "A serial appears twice."));
        if (item.SerialTracked && serials.Length != (int)quantity)
        {
            return (0, [], Error.Validation("SERIALS_REQUIRED", $"{item.Sku} is serial-tracked: give one serial per unit."));
        }

        if (!item.SerialTracked && serials.Length > 0) return (0, [], Error.Validation("SERIALS_NOT_TRACKED", $"{item.Sku} is not serial-tracked."));
        return (quantity, serials, null);
    }
}
