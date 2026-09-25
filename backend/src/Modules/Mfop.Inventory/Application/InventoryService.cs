using System.Globalization;
using Mfop.Evidence.Contracts;
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
/// The inventory ledger (docs/lean-pilot/07): RECEIPT, CONSUME on a work order, REVERSAL of a receipt, and the movement
/// helpers used by the M5 document flows (StockDocumentService). Every movement is an append-only ledger row; balances are
/// updated in the same transaction and the verifier recomputes them from the movements.
/// </summary>
public sealed class InventoryService(InventoryDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IEvidenceRegistry evidence)
{
    public const string CoreReturnOwnerType = "CORE_RETURN", StockIssueOwnerType = "STOCK_ISSUE";

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

    /// <summary>S4: a REVERSAL of a receipt (WAREHOUSE → SUPPLIER). The original stays as it is. Issues are cancelled as documents (S5).</summary>
    public Task<Result<Guid>> ReverseReceiptAsync(CommandEnvelope cmd, Guid movementId, ReverseRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason)) return Error.Validation("REASON_REQUIRED", "A reason is required for a reversal.");
            var original = await db.Movements.SingleOrDefaultAsync(m => m.Id == movementId, innerCt);
            if (original is null || original.Type != MovementTypes.Receipt) return Error.NotFound("MOVEMENT_NOT_FOUND", "Movement not found.");
            if (await db.Movements.AnyAsync(m => m.ReversalOf == movementId, innerCt)) return Error.Conflict("ALREADY_REVERSED", "This movement was already reversed.");
            var command = LedgerCommands.ReverseReceipt;
            var from = await db.Locations.SingleAsync(l => l.Id == original.ToLocationId, innerCt);
            var to = await db.Locations.SingleAsync(l => l.Id == original.FromLocationId, innerCt);
            var t = InventoryStateMachines.StockLedger.Resolve(from.Kind, command);
            if (!t.IsSuccess) return t.Error!;
            if (await BalanceAsync(from.Id, original.ItemId, innerCt) < original.Quantity)
            {
                return Error.Validation("INSUFFICIENT_STOCK", "The quantity is no longer at the location; it cannot be reversed.");
            }

            var item0 = await db.Items.SingleAsync(i => i.Id == original.ItemId, innerCt);
            if (item0.SerialTracked)
            {
                var located = await SerialLocationsAsync(item0.Id, innerCt);
                if (original.Serials.Any(x => !located.TryGetValue(x, out var loc) || loc != from.Id))
                {
                    return Error.Validation("SERIAL_NOT_IN_WAREHOUSE", "A serial of this receipt has left the warehouse; it cannot be reversed.");
                }
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
        var occurredAt = ClockExtensions.TruncateToMicros(request.OccurredAt);
        var id = await MoveAsync(cmd, MovementTypes.Consume, item, quantity, custody, consumed, serials, request.WorkOrderId, request.AssetId, null,
            occurredAt, null, ct, request.MovementId, exceeds, serialNotInCustody);

        // M5: which unit is on the light now (serial_units.installed_asset_id), and the failed part that must come back.
        var cores = new List<Guid>();
        if (item.SerialTracked)
        {
            var previouslyInstalled = await db.SerialUnits
                .Where(u => u.ItemId == item.Id && u.InstalledAssetId == request.AssetId && !serials.Contains(u.Serial))
                .OrderBy(u => u.InstalledAt).ThenBy(u => u.Serial).ToListAsync(ct);
            for (var i = 0; i < serials.Length; i++)
            {
                var removed = i < previouslyInstalled.Count ? previouslyInstalled[i] : null;
                if (removed is not null)
                {
                    removed.Remove(request.AssetId, occurredAt);
                    audit.Record(removed, "serial_unit.removed", extra: new Dictionary<string, object?> { ["replaced_by"] = serials[i], ["movement_id"] = id.ToString("D") });
                }

                var unit = await UnitAsync(item.Id, serials[i], false, ct);
                unit.Install(request.AssetId, id, occurredAt);
                audit.Record(unit, "serial_unit.installed", extra: new Dictionary<string, object?> { ["asset_id"] = request.AssetId.ToString("D"), ["movement_id"] = id.ToString("D") });
                if (item.CoreReturnRequired)
                {
                    cores.Add(await OpenCoreReturnAsync(request, item, id, 1m, serials[i], removed?.Serial, ct));
                }
            }
        }
        else if (item.CoreReturnRequired)
        {
            cores.Add(await OpenCoreReturnAsync(request, item, id, quantity, null, null, ct));
        }

        return new ConsumeOutcome(id, exceeds, serialNotInCustody, before, cores);
    }

    /// <summary>CR1: the obligation to bring the failed part back opens with the consumption (07 §7.2).</summary>
    private async Task<Guid> OpenCoreReturnAsync(ConsumeRequest request, Item item, Guid movementId, decimal quantity, string? installedSerial, string? expectedSerial,
        CancellationToken ct)
    {
        var t = StockDocumentMachines.CoreReturn.Create(DocumentCommands.ConsumeMaterial);
        if (!t.IsSuccess) throw new InvalidOperationException(t.Error!.Message);
        var code = $"CORE-{await NextAsync("core_return_code_seq", ct):D6}";
        var core = new CoreReturn(Ids.New(), code, request.WorkOrderId, request.AssetId, item.Id, request.TechnicianId, movementId, quantity, installedSerial,
            expectedSerial, clock.UtcNowMicros(), request.WorkOrderCode, request.AssetCode);
        db.CoreReturns.Add(core);
        audit.Record(core, "core_return.opened", extra: new Dictionary<string, object?>
        {
            ["sku"] = item.Sku,
            ["work_order_id"] = request.WorkOrderId.ToString("D"),
            ["expected_serial"] = expectedSerial,
            ["installed_serial"] = installedSerial,
        });
        return core.Id;
    }

    /// <summary>
    /// DeclareCoreReturn inside the caller's unit of work (the WorkOrders module checked the work order state and the actor).
    /// WITH_PART needs a photo (serial optional); UNRECOVERABLE needs a reason and a photo. When the light had no known
    /// installed serial, the declared serial is recorded as its historical installed serial (07 §7.2).
    /// </summary>
    internal async Task<Result<Guid>> DeclareCoreReturnInCurrentUnitOfWorkAsync(DeclareCoreReturnRequest request, CancellationToken ct)
    {
        var core = await db.CoreReturns.SingleOrDefaultAsync(c => c.Id == request.CoreReturnId, ct);
        if (core is null) return Error.NotFound("CORE_RETURN_NOT_FOUND", "Core return not found.");
        var outcome = request.Outcome?.Trim().ToUpperInvariant();
        if (outcome is not (CoreReturnOutcomes.WithPart or CoreReturnOutcomes.Unrecoverable))
        {
            return Error.Validation("INVALID_OUTCOME", "Outcome must be WITH_PART or UNRECOVERABLE.");
        }

        if (request.Photo is not { } photo || photo.EvidenceId == Guid.Empty) return Error.Validation("PHOTO_REQUIRED", "A photo of the removed part (or of why it cannot be recovered) is required.");
        var item = await db.Items.SingleAsync(i => i.Id == core.ItemId, ct);
        var serial = string.IsNullOrWhiteSpace(request.Serial) ? null : request.Serial.Trim();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (serial is { Length: > 60 }) return Error.Validation("INVALID_SERIAL", "A serial is at most 60 characters.");
        if (reason is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "The reason is at most 1000 characters.");
        if (serial is not null && !item.SerialTracked) return Error.Validation("SERIALS_NOT_TRACKED", $"{item.Sku} is not serial-tracked.");

        Result<Kernel.StateMachines.Transition> t;
        if (outcome == CoreReturnOutcomes.WithPart)
        {
            t = core.DeclareWithPart(request.ActorId, request.At, serial, photo.EvidenceId);
        }
        else
        {
            if (reason is not { Length: >= 3 }) return Error.Validation("REASON_REQUIRED", "Say why the failed part cannot be brought back.");
            t = core.DeclareUnrecoverable(request.ActorId, request.At, reason, photo.EvidenceId);
        }

        if (!t.IsSuccess) return t.Error!;
        var announced = await evidence.AnnounceAsync(new AnnounceEvidence(photo.EvidenceId, CoreReturnOwnerType, core.Id, "CORE_RETURN", photo.Sha256 ?? string.Empty,
            photo.SizeBytes, photo.Mime ?? "image/jpeg", request.ActorId, request.DeviceId, photo.CapturedAt ?? request.At, null, null), ct);
        if (!announced.IsSuccess) return announced.Error!;
        audit.Record(core, outcome == CoreReturnOutcomes.WithPart ? "core_return.declared_with_part" : "core_return.declared_unrecoverable", reason,
            extra: new Dictionary<string, object?> { ["declared_serial"] = serial, ["evidence_id"] = photo.EvidenceId.ToString("D") });

        if (outcome == CoreReturnOutcomes.WithPart && core.ExpectedSerial is null && serial is not null
            && !await db.SerialUnits.AnyAsync(u => u.ItemId == item.Id && u.Serial == serial, ct))
        {
            var historical = new SerialUnit(Ids.New(), item.Id, serial, true);
            historical.Remove(core.AssetId, request.At);
            db.SerialUnits.Add(historical);
            audit.Record(historical, "serial_unit.historical_recorded", extra: new Dictionary<string, object?> { ["asset_id"] = core.AssetId.ToString("D"), ["core_return_id"] = core.Id.ToString("D") });
        }

        return core.Id;
    }

    /// <summary>Next number of a document sequence (a fixed name from this module, never user input).</summary>
    internal async Task<long> NextAsync(string sequence, CancellationToken ct) => sequence switch
    {
        "stock_issue_code_seq" => await db.Database.SqlQuery<long>($"SELECT nextval('inventory.stock_issue_code_seq') AS \"Value\"").SingleAsync(ct),
        "stock_return_code_seq" => await db.Database.SqlQuery<long>($"SELECT nextval('inventory.stock_return_code_seq') AS \"Value\"").SingleAsync(ct),
        "core_return_code_seq" => await db.Database.SqlQuery<long>($"SELECT nextval('inventory.core_return_code_seq') AS \"Value\"").SingleAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(sequence)),
    };

    private async Task<SerialUnit> UnitAsync(Guid itemId, string serial, bool historical, CancellationToken ct)
    {
        var unit = db.SerialUnits.Local.SingleOrDefault(u => u.ItemId == itemId && u.Serial == serial)
                   ?? await db.SerialUnits.SingleOrDefaultAsync(u => u.ItemId == itemId && u.Serial == serial, ct);
        if (unit is not null) return unit;
        unit = new SerialUnit(Ids.New(), itemId, serial, historical);
        db.SerialUnits.Add(unit);
        return unit;
    }

    internal async Task<Guid> MoveAsync(CommandEnvelope cmd, string type, Item item, decimal quantity, StockLocation from, StockLocation to, string[] serials,
        Guid? workOrderId, Guid? assetId, Guid? reversalOf, DateTimeOffset occurredAt, string? note, CancellationToken ct,
        Guid? id = null, bool exceedsCustody = false, bool serialNotInCustody = false, Guid? documentId = null)
    {
        var movement = new StockMovement(id ?? Ids.New(), type, item.Id, quantity, from.Id, to.Id, serials, workOrderId, assetId, reversalOf,
            cmd.Actor.UserId!.Value, cmd.CommandId, occurredAt, clock.UtcNowMicros(), string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            exceedsCustody, serialNotInCustody, documentId);
        db.Movements.Add(movement);
        audit.Record(movement, $"stock_movement.{type.ToLowerInvariant()}", extra: new Dictionary<string, object?>
        {
            ["sku"] = item.Sku,
            ["quantity"] = quantity.ToString("0.000", CultureInfo.InvariantCulture),
            ["from_kind"] = from.Kind,
            ["to_kind"] = to.Kind,
            ["document_id"] = documentId?.ToString("D"),
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

    internal async Task<decimal> BalanceAsync(Guid locationId, Guid itemId, CancellationToken ct) =>
        (db.Balances.Local.SingleOrDefault(b => b.LocationId == locationId && b.ItemId == itemId)
         ?? await db.Balances.AsNoTracking().SingleOrDefaultAsync(b => b.LocationId == locationId && b.ItemId == itemId, ct))?.Quantity ?? 0m;

    internal async Task<StockLocation> EnsureLocationAsync(string kind, Guid? custodian, CancellationToken ct)
    {
        var location = db.Locations.Local.SingleOrDefault(l => l.Kind == kind && l.CustodianUserId == custodian)
                       ?? await db.Locations.SingleOrDefaultAsync(l => l.Kind == kind && l.CustodianUserId == custodian, ct);
        if (location is not null) return location;
        location = new StockLocation(Ids.New(), kind, kind switch
        {
            LocationKinds.Warehouse => "المستودع",
            LocationKinds.Supplier => "المورّد",
            LocationKinds.Consumed => "مستهلك في أوامر العمل",
            LocationKinds.InTransit => "قيد التسليم",
            LocationKinds.CoreBin => "القطع التالفة المرتجعة",
            LocationKinds.Field => "قطع مفكوكة من الأعمدة",
            _ => "عهدة فني",
        }, custodian);
        db.Locations.Add(location);
        audit.Record(location, "stock_location.created");
        return location;
    }

    /// <summary>Where each serial of an item is now: the destination of the latest movement that carried it.</summary>
    internal async Task<Dictionary<string, Guid>> SerialLocationsAsync(Guid itemId, CancellationToken ct)
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

    internal static (decimal Quantity, string[] Serials, Error? Error) ValidateLine(Item item, StockLine line)
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
