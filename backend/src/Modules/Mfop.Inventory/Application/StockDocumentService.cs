using System.Globalization;
using Mfop.Access.Contracts;
using Mfop.Evidence.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Domain;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Rules.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Inventory.Application;

public sealed record OffboardingReturnResult(Guid? ReturnId, string? Code, IReadOnlyList<CustodyItem> Remaining);

/// <summary>
/// The M5 material accountability flows (docs/lean-pilot/04 §4.9–4.11, 07 §7.2), every one a command through its document
/// state machine and ledger movements in one audited transaction:
///   issue → IN_TRANSIT → technician acknowledges (custody) or disputes → warehouse cancels (REVERSAL);
///   unused return: technician declares (→ IN_TRANSIT) → warehouse receives (serials matched) or rejects (REVERSAL);
///   core return: warehouse receives the failed part (serial checked) or records it not presented; supervisor waives;
///   offboarding: the warehouse records what a disabled technician handed back (no free adjustment; the rest stays flagged).
/// Technician commands arrive through /sync/v1/push and run inside the Sync pipeline's unit of work.
/// </summary>
public sealed class StockDocumentService(
    InventoryDbContext db,
    IUnitOfWork uow,
    IAuditTrail audit,
    TimeProvider clock,
    InventoryService ledger,
    IAssigneeDirectory assignees,
    IUserDirectory users,
    IEvidenceRegistry evidence,
    IFlags flags)
{
    public const string NotReceiver = "NOT_RECEIVER";

    // ---- Stock issue (04 §4.9) -------------------------------------------------------------------------------------------

    /// <summary>SI1 / S2: WAREHOUSE → IN_TRANSIT per line. Issuer ≠ receiver; the warehouse never goes below zero (N14).</summary>
    public Task<Result<IssueCreated>> IssueAsync(CommandEnvelope cmd, IssueRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<IssueCreated>(cmd, async innerCt =>
        {
            if (request.Lines is not { Count: > 0 }) return Error.Validation("LINES_REQUIRED", "At least one line is required.");
            if (request.TechnicianId == cmd.Actor.UserId) return Error.Sod("The warehouse officer issuing stock cannot be the receiving technician.");
            var check = await assignees.CheckAsync(request.TechnicianId, innerCt);
            if (!check.Exists) return Error.NotFound("TECHNICIAN_NOT_FOUND", "User not found.");
            if (!check.IsActive || !check.IsTechnician) return Error.Validation("RECEIVER_NOT_TECHNICIAN", "Stock can only be issued to an active technician.");
            var note = Clean(request.Note);
            var requestedBy = Clean(request.RequestedBy);
            if (note is { Length: > 1000 } || requestedBy is { Length: > 200 }) return Error.Validation("NOTE_TOO_LONG", "Note or requester too long.");
            var duplicate = DuplicateItem(request.Lines);
            if (duplicate is not null) return duplicate;
            var t = StockDocumentMachines.StockIssue.Create(DocumentCommands.IssueStock);
            if (!t.IsSuccess) return t.Error!;

            var warehouse = await ledger.EnsureLocationAsync(LocationKinds.Warehouse, null, innerCt);
            var inTransit = await ledger.EnsureLocationAsync(LocationKinds.InTransit, null, innerCt);
            var now = clock.UtcNowMicros();
            var issue = new StockIssue(Ids.New(), $"ISS-{await ledger.NextAsync("stock_issue_code_seq", innerCt):D6}", request.TechnicianId, cmd.Actor.UserId!.Value,
                now, note, request.ReferenceWorkOrderId, requestedBy);
            db.Issues.Add(issue);
            audit.Record(issue, "stock_issue.issued", extra: new Dictionary<string, object?> { ["technician_id"] = request.TechnicianId.ToString("D") });

            var ids = new List<Guid>();
            foreach (var line in request.Lines)
            {
                var item = await db.Items.SingleOrDefaultAsync(i => i.Id == line.ItemId, innerCt);
                if (item is null) return Error.NotFound("ITEM_NOT_FOUND", "Item not found.");
                var (quantity, serials, error) = InventoryService.ValidateLine(item, line);
                if (error is not null) return error;
                var available = await ledger.BalanceAsync(warehouse.Id, item.Id, innerCt);
                if (available < quantity)
                {
                    return Error.Validation("INSUFFICIENT_STOCK", $"The warehouse holds {Q(available)} {item.Unit} of {item.Sku}.",
                        new Dictionary<string, string> { ["sku"] = item.Sku, ["available"] = Q(available), ["requested"] = Q(quantity) });
                }

                if (item.SerialTracked)
                {
                    var located = await ledger.SerialLocationsAsync(item.Id, innerCt);
                    var missing = serials.Where(s => !located.TryGetValue(s, out var loc) || loc != warehouse.Id).ToList();
                    if (missing.Count > 0) return Error.Validation("SERIAL_NOT_IN_WAREHOUSE", $"Serial(s) not in the warehouse: {string.Join(", ", missing)}.");
                }

                ids.Add(await ledger.MoveAsync(cmd, MovementTypes.Issue, item, quantity, warehouse, inTransit, serials, null, null, null, now, note, innerCt,
                    documentId: issue.Id));
            }

            return new IssueCreated(issue.Id, issue.Code, ids);
        }, ct);

    /// <summary>SI2 / S6 (phone): IN_TRANSIT → CUSTODY. Every serial of the issue must be confirmed (scan or tick); otherwise dispute.</summary>
    public async Task<Result<SyncCommandOutcome>> AcknowledgeAsync(CommandEnvelope cmd, Guid issueId, IReadOnlyList<string>? confirmedSerials, CancellationToken ct)
    {
        var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, ct);
        if (issue is null) return Error.NotFound("STOCK_ISSUE_NOT_FOUND", "Stock issue not found.");
        if (issue.TechnicianId != cmd.Actor.UserId) return Error.Forbidden(NotReceiver, "This issue is addressed to another technician.");
        var at = At(cmd);
        var t = issue.Acknowledge(at);
        if (!t.IsSuccess) return t.Error!;

        var lines = await db.Movements.Where(m => m.DocumentId == issue.Id && m.Type == MovementTypes.Issue).OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var issued = lines.SelectMany(m => m.Serials).ToHashSet(StringComparer.Ordinal);
        var confirmed = (confirmedSerials ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (!issued.SetEquals(confirmed))
        {
            return Error.Validation("SERIALS_NOT_CONFIRMED", "Confirm exactly the serials you received; if something is missing or different, dispute the issue.",
                new Dictionary<string, string>
                {
                    ["missing"] = string.Join(",", issued.Except(confirmed).Order(StringComparer.Ordinal)),
                    ["unexpected"] = string.Join(",", confirmed.Except(issued).Order(StringComparer.Ordinal)),
                });
        }

        var inTransit = await ledger.EnsureLocationAsync(LocationKinds.InTransit, null, ct);
        var custody = await ledger.EnsureLocationAsync(LocationKinds.Custody, issue.TechnicianId, ct);
        foreach (var line in lines)
        {
            var item = await db.Items.SingleAsync(i => i.Id == line.ItemId, ct);
            await ledger.MoveAsync(cmd, MovementTypes.Acknowledge, item, line.Quantity, inTransit, custody, line.Serials, null, null, null, at, null, ct,
                documentId: issue.Id);
        }

        audit.Record(issue, "stock_issue.acknowledged");
        return new SyncCommandOutcome(issue.Id);
    }

    /// <summary>SI3 (phone): reason + optional photo; WARNING flag. The material stays in transit until the warehouse cancels.</summary>
    public async Task<Result<SyncCommandOutcome>> DisputeAsync(CommandEnvelope cmd, Guid issueId, string? reason, PhotoAnnouncement? photo, CancellationToken ct)
    {
        var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, ct);
        if (issue is null) return Error.NotFound("STOCK_ISSUE_NOT_FOUND", "Stock issue not found.");
        if (issue.TechnicianId != cmd.Actor.UserId) return Error.Forbidden(NotReceiver, "This issue is addressed to another technician.");
        var text = Clean(reason);
        if (text is not { Length: >= 3 }) return Error.Validation("REASON_REQUIRED", "Say what is wrong with this issue.");
        if (text.Length > 1000) return Error.Validation("NOTE_TOO_LONG", "The reason is at most 1000 characters.");
        var at = At(cmd);
        var t = issue.Dispute(text, photo?.EvidenceId, at);
        if (!t.IsSuccess) return t.Error!;
        if (photo is not null)
        {
            var announced = await evidence.AnnounceAsync(new AnnounceEvidence(photo.EvidenceId, InventoryService.StockIssueOwnerType, issue.Id, "DISPUTE",
                photo.Sha256 ?? string.Empty, photo.SizeBytes, photo.Mime ?? "image/jpeg", cmd.Actor.UserId!.Value, cmd.Actor.DeviceId, photo.CapturedAt ?? at, null, null), ct);
            if (!announced.IsSuccess) return announced.Error!;
        }

        audit.Record(issue, "stock_issue.disputed", text);
        await flags.RaiseAsync(FlagCodes.StockIssueDisputed, "WARNING", "stock_issue", issue.Id, new Dictionary<string, object?>
        {
            ["issue_code"] = issue.Code, ["technician_id"] = issue.TechnicianId.ToString("D"), ["reason"] = text,
            ["evidence_id"] = photo?.EvidenceId.ToString("D"),
        }, ct);
        return new SyncCommandOutcome(issue.Id);
    }

    /// <summary>SI4 / S5: REVERSAL IN_TRANSIT → WAREHOUSE of every line; the warehouse re-issues if needed.</summary>
    public Task<Result<Guid>> CancelIssueAsync(CommandEnvelope cmd, Guid issueId, ReverseRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var reason = Clean(request.Reason);
            if (reason is null) return Error.Validation("REASON_REQUIRED", "A reason is required to cancel an issue.");
            var issue = await db.Issues.SingleOrDefaultAsync(i => i.Id == issueId, innerCt);
            if (issue is null) return Error.NotFound("STOCK_ISSUE_NOT_FOUND", "Stock issue not found.");
            var now = clock.UtcNowMicros();
            var t = issue.Cancel(cmd.Actor.UserId!.Value, reason, now);
            if (!t.IsSuccess) return t.Error!;
            await ReverseLinesAsync(cmd, issue.Id, MovementTypes.Issue, LocationKinds.Warehouse, null, reason, now, innerCt);
            audit.Record(issue, "stock_issue.cancelled", reason);
            return issue.Id;
        }, ct);

    // ---- Unused return (04 §4.10) ----------------------------------------------------------------------------------------

    /// <summary>SR1 / S7 (phone): CUSTODY → IN_TRANSIT. Only what is in the technician's custody (quantity and serials).</summary>
    public async Task<Result<SyncCommandOutcome>> DeclareReturnAsync(CommandEnvelope cmd, Guid returnId, IReadOnlyList<StockLine>? lines, string? note, CancellationToken ct)
    {
        if (returnId == Guid.Empty) return Error.Validation("INVALID_PAYLOAD", "returnId is required.");
        if (lines is not { Count: > 0 }) return Error.Validation("LINES_REQUIRED", "At least one line is required.");
        if (await db.Returns.AnyAsync(r => r.Id == returnId, ct)) return Error.Conflict("RETURN_EXISTS", "This return id was already used.");
        var text = Clean(note);
        if (text is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "The note is at most 1000 characters.");
        var duplicate = DuplicateItem(lines);
        if (duplicate is not null) return duplicate;
        var t = StockDocumentMachines.StockReturn.Create(DocumentCommands.DeclareStockReturn);
        if (!t.IsSuccess) return t.Error!;

        var technician = cmd.Actor.UserId!.Value;
        var at = At(cmd);
        var custody = await ledger.EnsureLocationAsync(LocationKinds.Custody, technician, ct);
        var inTransit = await ledger.EnsureLocationAsync(LocationKinds.InTransit, null, ct);
        var ret = StockReturn.Declare(returnId, $"RET-{await ledger.NextAsync("stock_return_code_seq", ct):D6}", technician, at, text);
        db.Returns.Add(ret);
        audit.Record(ret, "stock_return.declared");
        foreach (var line in lines)
        {
            var moved = await FromCustodyAsync(cmd, line, custody, inTransit, MovementTypes.ReturnDeclare, ret.Id, at, ct);
            if (moved is not null) return moved;
        }

        return new SyncCommandOutcome(ret.Id);
    }

    /// <summary>SR2 / S8: IN_TRANSIT → WAREHOUSE; the serials counted by the officer must be exactly the declared ones.</summary>
    public Task<Result<Guid>> ReceiveReturnAsync(CommandEnvelope cmd, Guid returnId, ReceiveReturnRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var ret = await db.Returns.SingleOrDefaultAsync(r => r.Id == returnId, innerCt);
            if (ret is null) return Error.NotFound("STOCK_RETURN_NOT_FOUND", "Return not found.");
            if (ret.TechnicianId == cmd.Actor.UserId) return Error.Sod("The receiving warehouse officer cannot be the technician who returns the material.");
            var now = clock.UtcNowMicros();
            var t = ret.Receive(cmd.Actor.UserId!.Value, now);
            if (!t.IsSuccess) return t.Error!;
            var lines = await db.Movements.Where(m => m.DocumentId == ret.Id && m.Type == MovementTypes.ReturnDeclare).OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(innerCt);
            var declared = lines.SelectMany(m => m.Serials).ToHashSet(StringComparer.Ordinal);
            var counted = (request.ConfirmedSerials ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
            if (!declared.SetEquals(counted))
            {
                return Error.Validation("SERIALS_DO_NOT_MATCH", "The serials counted do not match the declared return; reject it with a reason instead.",
                    new Dictionary<string, string>
                    {
                        ["missing"] = string.Join(",", declared.Except(counted).Order(StringComparer.Ordinal)),
                        ["unexpected"] = string.Join(",", counted.Except(declared).Order(StringComparer.Ordinal)),
                    });
            }

            var inTransit = await ledger.EnsureLocationAsync(LocationKinds.InTransit, null, innerCt);
            var warehouse = await ledger.EnsureLocationAsync(LocationKinds.Warehouse, null, innerCt);
            foreach (var line in lines)
            {
                var item = await db.Items.SingleAsync(i => i.Id == line.ItemId, innerCt);
                await ledger.MoveAsync(cmd, MovementTypes.ReturnReceive, item, line.Quantity, inTransit, warehouse, line.Serials, null, null, null, now,
                    Clean(request.Note), innerCt, documentId: ret.Id);
            }

            audit.Record(ret, "stock_return.received");
            return ret.Id;
        }, ct);

    /// <summary>SR3 / S9: REVERSAL IN_TRANSIT → CUSTODY (the material is still the technician's); reason; WARNING flag.</summary>
    public Task<Result<Guid>> RejectReturnAsync(CommandEnvelope cmd, Guid returnId, ReverseRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var reason = Clean(request.Reason);
            if (reason is null) return Error.Validation("REASON_REQUIRED", "A reason is required to reject a return.");
            var ret = await db.Returns.SingleOrDefaultAsync(r => r.Id == returnId, innerCt);
            if (ret is null) return Error.NotFound("STOCK_RETURN_NOT_FOUND", "Return not found.");
            if (ret.TechnicianId == cmd.Actor.UserId) return Error.Sod("The warehouse officer cannot decide their own return.");
            var now = clock.UtcNowMicros();
            var t = ret.Reject(cmd.Actor.UserId!.Value, now, reason);
            if (!t.IsSuccess) return t.Error!;
            await ReverseLinesAsync(cmd, ret.Id, MovementTypes.ReturnDeclare, LocationKinds.Custody, ret.TechnicianId, reason, now, innerCt);
            audit.Record(ret, "stock_return.rejected", reason);
            await flags.RaiseAsync(FlagCodes.StockReturnRejected, "WARNING", "stock_return", ret.Id, new Dictionary<string, object?>
            {
                ["return_code"] = ret.Code, ["technician_id"] = ret.TechnicianId.ToString("D"), ["reason"] = reason,
            }, innerCt);
            return ret.Id;
        }, ct);

    // ---- Offboarding return (05 §5.6 step 8, M5) -------------------------------------------------------------------------

    /// <summary>
    /// SR4 / S12: what a DISABLED technician physically handed back, recorded by the warehouse officer (the technician can no
    /// longer act). Only quantities and serials that are in that custody; nothing is written off. INFO flag each time; with
    /// <c>final</c>, whatever is still in the custody is flagged CUSTODY_NOT_RETURNED (WARNING) for review.
    /// </summary>
    public Task<Result<OffboardingReturnResult>> OffboardingReturnAsync(CommandEnvelope cmd, OffboardingReturnRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<OffboardingReturnResult>(cmd, async innerCt =>
        {
            var note = Clean(request.Note);
            if (note is not { Length: >= 3 }) return Error.Validation("REASON_REQUIRED", "Describe the handover (who brought the material, when).");
            if (note.Length > 1000) return Error.Validation("NOTE_TOO_LONG", "The note is at most 1000 characters.");
            if (request.TechnicianId == cmd.Actor.UserId) return Error.Sod("The warehouse officer cannot record their own return.");
            var check = await assignees.CheckAsync(request.TechnicianId, innerCt);
            if (!check.Exists) return Error.NotFound("TECHNICIAN_NOT_FOUND", "User not found.");
            if ((await users.InactiveAsync([request.TechnicianId], innerCt)).Count == 0)
            {
                return Error.Validation("USER_STILL_ACTIVE", "An active technician declares returns on the phone; this is only for disabled users.");
            }

            var lines = request.Lines ?? [];
            if (lines.Count == 0 && !request.Final) return Error.Validation("LINES_REQUIRED", "Enter what was handed back, or mark the handover as final.");
            var duplicate = DuplicateItem(lines);
            if (duplicate is not null) return duplicate;

            var now = clock.UtcNowMicros();
            var custody = await ledger.EnsureLocationAsync(LocationKinds.Custody, request.TechnicianId, innerCt);
            var warehouse = await ledger.EnsureLocationAsync(LocationKinds.Warehouse, null, innerCt);
            StockReturn? ret = null;
            if (lines.Count > 0)
            {
                var t = StockDocumentMachines.StockReturn.Create(DocumentCommands.ReceiveOffboardingReturn);
                if (!t.IsSuccess) return t.Error!;
                ret = StockReturn.ReceivedOnOffboarding(Ids.New(), $"RET-{await ledger.NextAsync("stock_return_code_seq", innerCt):D6}", request.TechnicianId,
                    cmd.Actor.UserId!.Value, now, note);
                db.Returns.Add(ret);
                audit.Record(ret, "stock_return.offboarding_received", note);
                foreach (var line in lines)
                {
                    var moved = await FromCustodyAsync(cmd, line, custody, warehouse, MovementTypes.ReturnReceive, ret.Id, now, innerCt);
                    if (moved is not null) return moved;
                }
            }

            var remaining = await RemainingCustodyAsync(custody.Id, innerCt);
            var openCores = await db.CoreReturns.CountAsync(c => c.TechnicianId == request.TechnicianId
                && (c.State == CoreReturnStates.Open || c.State == CoreReturnStates.DeclaredWithPart), innerCt);
            var returned = string.Join(",", lines.Select(l => $"{l.ItemId:D}:{Q(Quantities.Normalize(l.Quantity))}"));
            await flags.RaiseAsync(FlagCodes.OffboardingReturnRecorded, "INFO", "user", request.TechnicianId, new Dictionary<string, object?>
            {
                ["return_code"] = ret?.Code, ["returned"] = returned, ["remaining"] = Summary(remaining), ["final"] = request.Final,
                ["open_core_returns"] = openCores, ["recorded_by"] = cmd.Actor.UserId?.ToString("D"),
            }, innerCt);
            if (request.Final && (remaining.Count > 0 || openCores > 0))
            {
                await flags.RaiseAsync(FlagCodes.CustodyNotReturned, "WARNING", "user", request.TechnicianId, new Dictionary<string, object?>
                {
                    ["items"] = Summary(remaining), ["open_core_returns"] = openCores,
                }, innerCt);
            }

            if (ret is null)
            {
                // Nothing moved: the flags above are the record (audited with the flag rows).
                return new OffboardingReturnResult(null, null, remaining);
            }

            return new OffboardingReturnResult(ret.Id, ret.Code, remaining);
        }, ct);

    // ---- Core return (04 §4.11) ------------------------------------------------------------------------------------------

    /// <summary>
    /// CR4 / S11: the failed part enters CORE_BIN (FIELD → CORE_BIN). Serial check: the presented serial must equal the reference
    /// (the known installed serial, else the declared one); a mismatch still records the receipt and raises CORE_SERIAL_MISMATCH
    /// (CRITICAL). CR5: NOT_PRESENTED → DECLARED_UNRECOVERABLE with a reason (WARNING flag).
    /// </summary>
    public Task<Result<Guid>> ReceiveCoreAsync(CommandEnvelope cmd, Guid coreReturnId, ReceiveCoreRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var core = await db.CoreReturns.SingleOrDefaultAsync(c => c.Id == coreReturnId, innerCt);
            if (core is null) return Error.NotFound("CORE_RETURN_NOT_FOUND", "Core return not found.");
            if (core.TechnicianId == cmd.Actor.UserId) return Error.Sod("The receiving warehouse officer cannot be the technician who removed the part.");
            var outcome = request.Outcome?.Trim().ToUpperInvariant();
            var now = clock.UtcNowMicros();
            var item = await db.Items.SingleAsync(i => i.Id == core.ItemId, innerCt);
            if (outcome == CoreReturnOutcomes.NotPresented)
            {
                var reason = Clean(request.Reason);
                if (reason is null) return Error.Validation("REASON_REQUIRED", "A reason is required when the part is not presented.");
                var np = core.NotPresented(cmd.Actor.UserId!.Value, now, reason);
                if (!np.IsSuccess) return np.Error!;
                audit.Record(core, "core_return.part_not_presented", reason);
                await flags.RaiseAsync(FlagCodes.CorePartNotPresented, "WARNING", "core_return", core.Id, new Dictionary<string, object?>
                {
                    ["core_return_code"] = core.Code, ["work_order_id"] = core.WorkOrderId.ToString("D"), ["technician_id"] = core.TechnicianId.ToString("D"),
                    ["sku"] = item.Sku, ["reason"] = reason,
                }, innerCt);
                return core.Id;
            }

            if (outcome != CoreReturnOutcomes.Received) return Error.Validation("INVALID_OUTCOME", "Outcome must be RECEIVED or NOT_PRESENTED.");
            var serial = string.IsNullOrWhiteSpace(request.Serial) ? null : request.Serial.Trim();
            if (item.SerialTracked && serial is null) return Error.Validation("SERIAL_REQUIRED", $"{item.Sku} is serial-tracked: read the serial on the failed part.");
            if (!item.SerialTracked && serial is not null) return Error.Validation("SERIALS_NOT_TRACKED", $"{item.Sku} is not serial-tracked.");
            if (serial is { Length: > 60 }) return Error.Validation("INVALID_SERIAL", "A serial is at most 60 characters.");
            if (serial is not null)
            {
                // A unit that is in stock (warehouse, in transit, a custody) cannot be a part removed from a light.
                var located = await ledger.SerialLocationsAsync(item.Id, innerCt);
                if (located.TryGetValue(serial, out var at))
                {
                    var kind = (await db.Locations.AsNoTracking().SingleAsync(l => l.Id == at, innerCt)).Kind;
                    if (kind is LocationKinds.Warehouse or LocationKinds.InTransit or LocationKinds.Custody or LocationKinds.CoreBin)
                    {
                        return Error.Validation("SERIAL_IN_STOCK", $"Serial {serial} is recorded at {kind}; it cannot be a part removed from a light.");
                    }
                }
            }

            var mismatch = item.SerialTracked && core.ReferenceSerial is { } reference && !string.Equals(reference, serial, StringComparison.OrdinalIgnoreCase);
            var t = StockDocumentMachines.CoreReturn.Resolve(core.State, DocumentCommands.ReceiveCorePart);
            if (!t.IsSuccess) return t.Error!;
            var field = await ledger.EnsureLocationAsync(LocationKinds.Field, null, innerCt);
            var bin = await ledger.EnsureLocationAsync(LocationKinds.CoreBin, null, innerCt);
            var movementId = await ledger.MoveAsync(cmd, MovementTypes.CoreReceive, item, core.Quantity, field, bin, serial is null ? [] : [serial], null, core.AssetId,
                null, now, Clean(request.Reason), innerCt, documentId: core.Id);
            core.Receive(cmd.Actor.UserId!.Value, now, serial, mismatch, movementId);
            audit.Record(core, "core_return.received", extra: new Dictionary<string, object?>
            {
                ["presented_serial"] = serial, ["reference_serial"] = core.ReferenceSerial, ["serial_mismatch"] = mismatch,
            });
            if (mismatch)
            {
                await flags.RaiseAsync(FlagCodes.CoreSerialMismatch, "CRITICAL", "core_return", core.Id, new Dictionary<string, object?>
                {
                    ["core_return_code"] = core.Code, ["work_order_id"] = core.WorkOrderId.ToString("D"), ["asset_id"] = core.AssetId.ToString("D"),
                    ["technician_id"] = core.TechnicianId.ToString("D"), ["sku"] = item.Sku, ["expected_serial"] = core.ExpectedSerial,
                    ["declared_serial"] = core.DeclaredSerial, ["presented_serial"] = serial,
                }, innerCt);
            }

            return core.Id;
        }, ct);

    /// <summary>CR6: SUPERVISOR ≠ the technician concerned; reason; always an INFO flag to the auditor (03 note 8).</summary>
    public Task<Result<Guid>> WaiveCoreAsync(CommandEnvelope cmd, Guid coreReturnId, ReverseRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var reason = Clean(request.Reason);
            if (reason is null) return Error.Validation("REASON_REQUIRED", "A reason is required to waive a core return.");
            var core = await db.CoreReturns.SingleOrDefaultAsync(c => c.Id == coreReturnId, innerCt);
            if (core is null) return Error.NotFound("CORE_RETURN_NOT_FOUND", "Core return not found.");
            if (core.TechnicianId == cmd.Actor.UserId || core.DeclaredBy == cmd.Actor.UserId)
            {
                return Error.Sod("A core return is waived by a supervisor other than the technician concerned.");
            }

            var now = clock.UtcNowMicros();
            var t = core.Waive(cmd.Actor.UserId!.Value, now, reason);
            if (!t.IsSuccess) return t.Error!;
            audit.Record(core, "core_return.waived", reason);
            var item = await db.Items.AsNoTracking().SingleAsync(i => i.Id == core.ItemId, innerCt);
            await flags.RaiseAsync(FlagCodes.CoreReturnWaived, "INFO", "core_return", core.Id, new Dictionary<string, object?>
            {
                ["core_return_code"] = core.Code, ["work_order_id"] = core.WorkOrderId.ToString("D"), ["technician_id"] = core.TechnicianId.ToString("D"),
                ["waived_by"] = cmd.Actor.UserId?.ToString("D"), ["sku"] = item.Sku, ["reason"] = reason,
            }, innerCt);
            return core.Id;
        }, ct);

    // ---- helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>Moves one line out of a technician's custody: never more than the custody holds, and only serials that are in it.</summary>
    private async Task<Error?> FromCustodyAsync(CommandEnvelope cmd, StockLine line, StockLocation custody, StockLocation to, string type, Guid documentId,
        DateTimeOffset at, CancellationToken ct)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == line.ItemId, ct);
        if (item is null) return Error.NotFound("ITEM_NOT_FOUND", "Item not found.");
        var (quantity, serials, error) = InventoryService.ValidateLine(item, line);
        if (error is not null) return error;
        var held = await ledger.BalanceAsync(custody.Id, item.Id, ct);
        if (held < quantity)
        {
            return Error.Validation("INSUFFICIENT_CUSTODY", $"The custody holds {Q(held)} {item.Unit} of {item.Sku}.",
                new Dictionary<string, string> { ["sku"] = item.Sku, ["available"] = Q(held), ["requested"] = Q(quantity) });
        }

        if (item.SerialTracked)
        {
            var located = await ledger.SerialLocationsAsync(item.Id, ct);
            var missing = serials.Where(s => !located.TryGetValue(s, out var loc) || loc != custody.Id).ToList();
            if (missing.Count > 0) return Error.Validation("SERIAL_NOT_IN_CUSTODY", $"Serial(s) not in this custody: {string.Join(", ", missing)}.");
        }

        await ledger.MoveAsync(cmd, type, item, quantity, custody, to, serials, null, null, null, at, null, ct, documentId: documentId);
        return null;
    }

    /// <summary>REVERSAL of each line of a document, back to where it came from.</summary>
    private async Task ReverseLinesAsync(CommandEnvelope cmd, Guid documentId, string lineType, string toKind, Guid? custodian, string reason, DateTimeOffset now,
        CancellationToken ct)
    {
        var lines = await db.Movements.Where(m => m.DocumentId == documentId && m.Type == lineType).OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var inTransit = await ledger.EnsureLocationAsync(LocationKinds.InTransit, null, ct);
        var to = await ledger.EnsureLocationAsync(toKind, custodian, ct);
        foreach (var line in lines)
        {
            var item = await db.Items.SingleAsync(i => i.Id == line.ItemId, ct);
            await ledger.MoveAsync(cmd, MovementTypes.Reversal, item, line.Quantity, inTransit, to, line.Serials, null, null, line.Id, now, reason, ct,
                documentId: documentId);
        }
    }

    private async Task<IReadOnlyList<CustodyItem>> RemainingCustodyAsync(Guid custodyId, CancellationToken ct)
    {
        var balances = db.Balances.Local.Where(b => b.LocationId == custodyId).ToList();
        var stored = await db.Balances.AsNoTracking().Where(b => b.LocationId == custodyId).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        return stored.Select(b => balances.FirstOrDefault(l => l.ItemId == b.ItemId) ?? b)
            .Concat(balances.Where(l => stored.All(s => s.ItemId != l.ItemId)))
            .Where(b => b.Quantity != 0)
            .Select(b =>
            {
                var i = items[b.ItemId];
                return new CustodyItem(i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, i.SerialTracked, b.Quantity, []);
            })
            .OrderBy(c => c.Sku, StringComparer.Ordinal).ToList();
    }

    private static Error? DuplicateItem(IReadOnlyList<StockLine> lines) =>
        lines.GroupBy(l => l.ItemId).Any(g => g.Count() > 1) ? Error.Validation("DUPLICATE_LINE", "Each item appears once; put all its quantity and serials on one line.") : null;

    private DateTimeOffset At(CommandEnvelope cmd) => ClockExtensions.TruncateToMicros(cmd.EstimatedTime ?? clock.GetUtcNow());

    private static string Summary(IReadOnlyList<CustodyItem> items) => string.Join(",", items.Select(c => $"{c.Sku}:{Q(c.Quantity)}"));

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Q(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
