using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Assets.Contracts;
using Mfop.Evidence.Contracts;
using Mfop.Inventory.Contracts;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Rules.Contracts;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Mfop.Kernel;

namespace Mfop.WorkOrders.Application;

/// <summary>
/// Technician commands on the phone (docs/lean-pilot/04 §4.8 W5, W6, W9, W12 and the in-state commands). They arrive
/// through the single write path (/sync/v1/push) and run inside the Sync pipeline's unit of work, so each command, its
/// idempotency receipt, its wo_event and its audit events commit together. Only the current assignee may act (N5).
/// </summary>
public sealed class FieldExecutionService(
    WorkOrdersDbContext db,
    IAuditTrail audit,
    TimeProvider clock,
    IAssetDirectory assets,
    IAssetStatusEvents assetStatus,
    IEvidenceRegistry evidence,
    IInventoryLedger inventory,
    IFlags flags,
    WorkOrderReferenceData reference)
{
    public const string NotAssignee = "NOT_ASSIGNEE";

    /// <summary>Conflict codes (docs/spec/10 §10.5). The phone shows them as "changed on the server", never as a silent loss.</summary>
    public const string WorkOrderChanged = "WORK_ORDER_CHANGED", WorkOrderReassigned = "WORK_ORDER_REASSIGNED",
        WorkOrderCancelled = "WORK_ORDER_CANCELLED", WorkOrderClosed = "WORK_ORDER_CLOSED", BaseVersionRequired = "BASE_VERSION_REQUIRED";

    /// <summary>
    /// Facts are things that happened in the field (a photo, a part used): they are kept even if the work order moved on.
    /// Changes set the work order's state or overwrite its fields: they are applied only on the version the technician saw.
    /// </summary>
    private enum FieldKind { Fact, Change }

    /// <summary>W5: the scanned tag must be this light's tag; a typed code needs a reason.</summary>
    public async Task<Result<SyncCommandOutcome>> ArriveAsync(CommandEnvelope cmd, ArriveOnSitePayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Change, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var w = loaded.Value.WorkOrder;
        var t = WorkOrderStateMachines.WorkOrder.Resolve(w.State, WorkOrderCommands.ArriveOnSite);
        if (!t.IsSuccess) return t.Error!;

        var asset = (await assets.GetAsync(w.AssetId, ct))!;
        bool tagScanned;
        string? scanned = null, manualCode = null, manualReason = null;
        if (!string.IsNullOrWhiteSpace(p.ScannedToken))
        {
            scanned = p.ScannedToken.Trim();
            if (scanned.Length > 200) return Error.Validation("TAG_NOT_FOUND", "This code is not a tag of ours.");
            var resolved = await assets.ResolveTagAsync(scanned, ct);
            if (resolved is null) return Error.Validation("TAG_NOT_FOUND", "This tag is not bound to any light.");
            if (resolved != w.AssetId)
            {
                var other = await assets.GetAsync(resolved.Value, ct);
                return Error.Validation("TAG_MISMATCH", $"This tag belongs to {other?.AssetCode ?? "another light"}, not {asset.AssetCode}.",
                    new Dictionary<string, string> { ["scannedAsset"] = other?.AssetCode ?? "?", ["expectedAsset"] = asset.AssetCode ?? "?" });
            }

            tagScanned = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(p.ManualCode)) return Error.Validation("TAG_SCAN_REQUIRED", "Scan the tag on the pole, or type its code with a reason.");
            if (string.IsNullOrWhiteSpace(p.ManualReason) || p.ManualReason.Trim().Length < 3)
            {
                return Error.Validation("MANUAL_CODE_REASON_REQUIRED", "Say why the tag could not be scanned.");
            }

            manualCode = p.ManualCode.Trim();
            manualReason = p.ManualReason.Trim();
            if (manualCode.Length > 60 || manualReason.Length > 500) return Error.Validation("INVALID_MANUAL_CODE", "Code or reason too long.");
            var matches = string.Equals(manualCode, asset.AssetCode, StringComparison.OrdinalIgnoreCase)
                          || await assets.ResolveTagAsync(manualCode, ct) == w.AssetId;
            if (!matches) return Error.Validation("CODE_MISMATCH", $"This code is not {asset.AssetCode}.");
            tagScanned = false;
        }

        var now = clock.UtcNowMicros();
        w.Arrive(now);
        if (w.ChecklistTemplateId is null && await reference.TemplateForAsync(asset.PowerSource, ct) is { } template)
        {
            w.PinChecklistTemplate(template.Id); // work orders created before M3
        }

        var arrivalNo = await db.Arrivals.CountAsync(a => a.WorkOrderId == w.Id, ct) + 1;
        var arrival = new WorkOrderArrival(Ids.New(), w.Id, cmd.Actor.UserId!.Value, arrivalNo, tagScanned, scanned, manualCode, manualReason,
            Device(cmd), now);
        db.Arrivals.Add(arrival);
        audit.Record(w, "work_order.arrived_on_site", manualReason);
        audit.Record(arrival, "work_order_arrival.recorded");
        WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.Arrived, cmd, now, new Dictionary<string, string?>
        {
            ["arrival_no"] = arrivalNo.ToString(CultureInfo.InvariantCulture),
            ["tag_scanned"] = tagScanned ? "true" : "false",
            ["manual_reason"] = manualReason,
        });
        return new SyncCommandOutcome(w.Id);
    }

    /// <summary>AddEvidence: announces the photo's hash (the bytes follow with PUT /sync/v1/evidence/{id}/content).</summary>
    public async Task<Result<SyncCommandOutcome>> AddEvidenceAsync(CommandEnvelope cmd, AddEvidencePayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Fact, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var (w, previousAssignee) = loaded.Value;
        var kind = p.Kind?.Trim().ToUpperInvariant();
        if (kind is not ("BEFORE" or "AFTER" or "TEST")) return Error.Validation("INVALID_EVIDENCE_KIND", "Kind must be BEFORE, AFTER or TEST.");
        if (!previousAssignee)
        {
            // The previous assignee's photos are kept whatever the state is now: the work happened (docs/spec/10 §10.5).
            var allowed = WorkOrderStateMachines.AllowedInState(w.State, WorkOrderCommands.AddEvidence);
            if (!allowed.IsSuccess) return allowed.Error!;
            if (kind == "BEFORE" && w.State != WorkOrderStates.OnSite) return Error.Validation("EVIDENCE_KIND_NOT_NOW", "Before photos are taken on arrival, before starting work.");
            if (kind != "BEFORE" && w.State == WorkOrderStates.OnSite) return Error.Validation("EVIDENCE_KIND_NOT_NOW", "After and test photos come after the work has started.");
        }

        var announced = await evidence.AnnounceAsync(new AnnounceEvidence(p.EvidenceId, EvidenceOwnerTypes.WorkOrder, w.Id, kind, p.Sha256 ?? string.Empty,
            p.SizeBytes, p.Mime ?? "image/jpeg", cmd.Actor.UserId!.Value, cmd.Actor.DeviceId, p.CapturedAt ?? cmd.DeviceTime,
            p.SupersedesEvidenceId, p.SupersedeReason), ct);
        if (!announced.IsSuccess) return announced.Error!;

        var now = clock.UtcNowMicros();
        audit.Record(w, "work_order.evidence_added");
        WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.EvidenceAdded, cmd, now, new Dictionary<string, string?>
        {
            ["evidence_id"] = p.EvidenceId.ToString("D"),
            ["kind"] = kind,
            ["sha256_device"] = p.Sha256?.ToLowerInvariant(),
            ["supersedes"] = p.SupersedesEvidenceId?.ToString("D"),
            ["by_previous_assignee"] = previousAssignee ? "true" : null,
        });
        if (previousAssignee) await FlagPreviousAssigneeAsync(cmd, w, WorkOrderCommands.AddEvidence, p.EvidenceId, ct);
        return new SyncCommandOutcome(p.EvidenceId);
    }

    /// <summary>RecordDiagnosis (ON_SITE, IN_PROGRESS): a seeded code for this light's power source.</summary>
    public async Task<Result<SyncCommandOutcome>> RecordDiagnosisAsync(CommandEnvelope cmd, RecordDiagnosisPayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Change, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var w = loaded.Value.WorkOrder;
        var allowed = WorkOrderStateMachines.AllowedInState(w.State, WorkOrderCommands.RecordDiagnosis);
        if (!allowed.IsSuccess) return allowed.Error!;

        var code = await db.DiagnosisCodes.AsNoTracking().SingleOrDefaultAsync(d => d.Code == p.Code, ct);
        if (code is null) return Error.Validation("UNKNOWN_DIAGNOSIS", "Unknown diagnosis code.");
        if (code.Path != DiagnosisPaths.Repair) return Error.Validation("DIAGNOSIS_NOT_AVAILABLE", "This outcome (no work) is not available before M6.");
        var asset = (await assets.GetAsync(w.AssetId, ct))!;
        if (!code.PowerSources.Contains(asset.PowerSource)) return Error.Validation("DIAGNOSIS_NOT_FOR_POWER_SOURCE", "This diagnosis does not apply to this light's power source.");
        var note = string.IsNullOrWhiteSpace(p.Note) ? null : p.Note.Trim();
        if (note is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "The note is at most 1000 characters.");

        w.RecordDiagnosis(code.Code, note);
        var now = clock.UtcNowMicros();
        audit.Record(w, "work_order.diagnosis_recorded");
        WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.DiagnosisRecorded, cmd, now, new Dictionary<string, string?> { ["code"] = code.Code, ["note"] = note });
        return new SyncCommandOutcome(w.Id);
    }

    /// <summary>W6: diagnosis recorded and the before photos of this arrival announced. Effect OS5: the light is UNDER_MAINTENANCE.</summary>
    public async Task<Result<SyncCommandOutcome>> StartWorkAsync(CommandEnvelope cmd, StartWorkPayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Change, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var w = loaded.Value.WorkOrder;
        var t = WorkOrderStateMachines.WorkOrder.Resolve(w.State, WorkOrderCommands.StartWork);
        if (!t.IsSuccess) return t.Error!;
        if (w.DiagnosisCode is null) return Error.Validation("DIAGNOSIS_REQUIRED", "Record the diagnosis before starting work.");

        var requirements = await reference.RequirementsForAsync(w.ChecklistTemplateId, ct);
        var arrivals = await db.Arrivals.AsNoTracking().Where(a => a.WorkOrderId == w.Id).OrderBy(a => a.ArrivalNo).ToListAsync(ct);
        var lastArrival = arrivals.LastOrDefault();
        var photos = (await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, [w.Id], ct))
            .Count(e => e.Kind == "BEFORE" && e.SupersededBy is null && Usable(e.State) && lastArrival is not null && e.AnnouncedAt >= lastArrival.RecordedAt);
        var required = arrivals.Count <= 1 ? requirements.BeforeFirstArrival : requirements.BeforeLaterArrival;
        if (photos < required)
        {
            return Error.Validation("EVIDENCE_MISSING", $"{required} before photo(s) are required on this arrival; {photos} taken.",
                new Dictionary<string, string> { ["kind"] = "BEFORE", ["required"] = required.ToString(CultureInfo.InvariantCulture), ["present"] = photos.ToString(CultureInfo.InvariantCulture) });
        }

        var now = clock.UtcNowMicros();
        w.StartWork(now);
        audit.Record(w, "work_order.work_started");
        await assetStatus.ApplyAsync(w.AssetId, AssetStatusCommands.OnWorkStarted, "WORK_ORDER", w.Id, ct);
        WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.WorkStarted, cmd, now);
        return new SyncCommandOutcome(w.Id);
    }

    /// <summary>ConsumeMaterial (IN_PROGRESS, RETURNED): custody → consumed, linked to the work order and the light.</summary>
    public async Task<Result<SyncCommandOutcome>> ConsumeMaterialAsync(CommandEnvelope cmd, ConsumeMaterialPayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Fact, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var (w, previousAssignee) = loaded.Value;
        if (!previousAssignee)
        {
            var allowed = WorkOrderStateMachines.AllowedInState(w.State, WorkOrderCommands.ConsumeMaterial);
            if (!allowed.IsSuccess) return allowed.Error!;
        }

        if (p.MovementId == Guid.Empty) return Error.Validation("INVALID_PAYLOAD", "movementId is required.");

        var now = clock.UtcNowMicros();
        var asset = await assets.GetAsync(w.AssetId, ct);
        var consumed = await inventory.ConsumeAsync(new ConsumeRequest(p.MovementId, p.ItemId, p.Quantity, p.Serials ?? [], cmd.Actor.UserId!.Value, w.Id,
            w.AssetId, cmd.DeviceTime ?? now, cmd.CommandId, w.Code, asset?.AssetCode), ct);
        if (!consumed.IsSuccess) return consumed.Error!;

        var outcome = consumed.Value;
        if (outcome.ExceedsCustody)
        {
            await flags.RaiseAsync(FlagCodes.ConsumptionExceedsCustody, "WARNING", "work_order", w.Id, new Dictionary<string, object?>
            {
                ["movement_id"] = outcome.MovementId.ToString("D"),
                ["item_id"] = p.ItemId.ToString("D"),
                ["quantity"] = WorkOrderSeal.Quantity(p.Quantity),
                ["custody_before"] = WorkOrderSeal.Quantity(outcome.CustodyBefore),
            }, ct);
        }

        if (outcome.SerialNotInCustody)
        {
            await flags.RaiseAsync(FlagCodes.SerialNotInCustody, "WARNING", "work_order", w.Id, new Dictionary<string, object?>
            {
                ["movement_id"] = outcome.MovementId.ToString("D"),
                ["serials"] = string.Join(",", p.Serials ?? []),
            }, ct);
        }

        audit.Record(w, "work_order.material_consumed");
        WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.MaterialConsumed, cmd, now, new Dictionary<string, string?>
        {
            ["movement_id"] = outcome.MovementId.ToString("D"),
            ["item_id"] = p.ItemId.ToString("D"),
            ["quantity"] = WorkOrderSeal.Quantity(p.Quantity),
            ["serials"] = string.Join(",", p.Serials ?? []),
            ["exceeds_custody"] = outcome.ExceedsCustody ? "true" : "false",
            ["by_previous_assignee"] = previousAssignee ? "true" : null,
            ["core_return_ids"] = outcome.CoreReturnIds is { Count: > 0 } cores ? string.Join(",", cores.Select(c => c.ToString("D"))) : null,
        });
        if (previousAssignee) await FlagPreviousAssigneeAsync(cmd, w, WorkOrderCommands.ConsumeMaterial, outcome.MovementId, ct);
        return new SyncCommandOutcome(outcome.MovementId, outcome.ExceedsCustody ? "APPLIED_EXCEEDS_CUSTODY" : outcome.CoreReturnIds is { Count: > 0 } ? "CORE_RETURN_OPENED" : null);
    }

    /// <summary>
    /// DeclareCoreReturn (04 §4.11; IN_PROGRESS, RETURNED, and after close until the part is received): the failed part is
    /// declared WITH_PART (photo, serial) or UNRECOVERABLE (reason, photo). A fact from the field: kept even if the work order
    /// moved on. By the technician who consumed the new part, or the current assignee. A closed work order is sealed, so no
    /// work-order event is added then (the core return's own audit trail records it).
    /// </summary>
    public async Task<Result<SyncCommandOutcome>> DeclareCoreReturnAsync(CommandEnvelope cmd, DeclareCoreReturnPayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Fact, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var (w, previousAssignee) = loaded.Value;
        var candidates = (await inventory.CoreReturnsForWorkOrdersAsync([w.Id], ct))
            .Where(c => p.CoreReturnId is { } id ? c.Id == id
                : p.MovementId is { } movement && c.ConsumeMovementId == movement
                  && (string.IsNullOrWhiteSpace(p.InstalledSerial) || string.Equals(c.InstalledSerial, p.InstalledSerial.Trim(), StringComparison.Ordinal)))
            .ToList();
        if (candidates.Count > 1) return Error.Validation("INSTALLED_SERIAL_REQUIRED", "This consumption installed several units: name the installed serial.");
        var core = candidates.SingleOrDefault();
        if (core is null) return Error.NotFound("CORE_RETURN_NOT_FOUND", "No core return of this work order matches.");
        var userId = cmd.Actor.UserId!.Value;
        if (core.TechnicianId != userId && w.AssigneeId != userId) return Error.Forbidden(NotAssignee, "Only the technician who replaced the part, or the assignee, declares it.");
        if (w.State != WorkOrderStates.Closed && !previousAssignee)
        {
            var allowed = WorkOrderStateMachines.AllowedInState(w.State, WorkOrderCommands.DeclareCoreReturn);
            if (!allowed.IsSuccess) return allowed.Error!;
        }

        var now = clock.UtcNowMicros();
        var declared = await inventory.DeclareCoreReturnAsync(new DeclareCoreReturnRequest(core.Id, p.Outcome, p.Serial, p.Reason, p.Photo, userId,
            cmd.Actor.DeviceId, cmd.EstimatedTime is { } t ? ClockExtensions.TruncateToMicros(t) : now), ct);
        if (!declared.IsSuccess) return declared.Error!;
        if (w.State != WorkOrderStates.Closed)
        {
            audit.Record(w, "work_order.core_return_declared");
            WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.CoreReturnDeclared, cmd, now, new Dictionary<string, string?>
            {
                ["core_return_id"] = core.Id.ToString("D"),
                ["outcome"] = p.Outcome?.Trim().ToUpperInvariant(),
                ["serial"] = string.IsNullOrWhiteSpace(p.Serial) ? null : p.Serial.Trim(),
                ["evidence_id"] = p.Photo?.EvidenceId.ToString("D"),
                ["by_previous_assignee"] = previousAssignee ? "true" : null,
            });
        }

        return new SyncCommandOutcome(core.Id);
    }

    /// <summary>RecordChecklist (IN_PROGRESS, RETURNED): the pinned template, every item answered, test method (NOT_TESTED needs a reason).</summary>
    public async Task<Result<SyncCommandOutcome>> RecordChecklistAsync(CommandEnvelope cmd, RecordChecklistPayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Change, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var w = loaded.Value.WorkOrder;
        var allowed = WorkOrderStateMachines.AllowedInState(w.State, WorkOrderCommands.RecordChecklist);
        if (!allowed.IsSuccess) return allowed.Error!;
        if (p.ChecklistId == Guid.Empty) return Error.Validation("INVALID_PAYLOAD", "checklistId is required.");
        if (w.ChecklistTemplateId is null || p.TemplateId != w.ChecklistTemplateId) return Error.Validation("CHECKLIST_TEMPLATE_MISMATCH", "Use the checklist pinned to this work order.");
        var template = (await reference.TemplateAsync(p.TemplateId, ct))!;
        if (template.TestMethods.All(m => m.Code != p.TestMethod)) return Error.Validation("INVALID_TEST_METHOD", "Unknown test method.");
        var notTested = p.TestMethod == TestMethods.NotTested;
        var reason = string.IsNullOrWhiteSpace(p.NotTestedReason) ? null : p.NotTestedReason.Trim();
        if (notTested && reason is null) return Error.Validation("NOT_TESTED_REASON_REQUIRED", "Say why the light could not be tested.");
        if (reason is { Length: > 500 }) return Error.Validation("NOTE_TOO_LONG", "The reason is at most 500 characters.");

        var answers = p.Answers ?? new Dictionary<string, string>();
        var unknown = answers.Keys.Where(k => template.Items.All(i => i.Code != k)).ToList();
        if (unknown.Count > 0) return Error.Validation("UNKNOWN_CHECKLIST_ITEM", $"Unknown item(s): {string.Join(", ", unknown)}.");
        var json = new JsonObject();
        var blocked = false;
        var testPassed = true;
        foreach (var item in template.Items)
        {
            if (notTested && item.TestItem) continue; // test items are not answered when the light could not be tested
            if (!answers.TryGetValue(item.Code, out var answer) || answer is not ("YES" or "NO"))
            {
                return Error.Validation("CHECKLIST_INCOMPLETE", $"Answer every item (missing: {item.Code}).");
            }

            json[item.Code] = answer;
            if (answer != item.Expected)
            {
                if (item.TestItem) testPassed = false;
                else if (item.Blocking) blocked = true;
            }
        }

        var testResult = notTested ? TestMethods.NotTested : testPassed ? "PASS" : "FAIL";
        var now = clock.UtcNowMicros();
        var result = new ChecklistResult(p.ChecklistId, w.Id, template.Id, p.TestMethod, reason, json.ToJsonString(), testResult, blocked,
            cmd.Actor.UserId!.Value, now);
        db.ChecklistResults.Add(result);
        w.RecordTest(p.TestMethod, testResult);
        audit.Record(w, "work_order.checklist_recorded", reason);
        audit.Record(result, "checklist_result.recorded");
        WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.ChecklistRecorded, cmd, now, new Dictionary<string, string?>
        {
            ["checklist_id"] = result.Id.ToString("D"),
            ["test_method"] = p.TestMethod,
            ["test_result"] = testResult,
            ["blocked"] = blocked ? "true" : "false",
        });
        return new SyncCommandOutcome(result.Id, blocked ? "CHECKLIST_BLOCKING" : testResult == "FAIL" ? "TEST_FAILED" : null);
    }

    /// <summary>W9 (SubmitWorkOrder) or W12 (Resubmit, additions only): the submission guards are evaluated again each time.</summary>
    public async Task<Result<SyncCommandOutcome>> SubmitAsync(CommandEnvelope cmd, string command, SubmitWorkOrderPayload p, CancellationToken ct)
    {
        var loaded = await LoadForFieldAsync(cmd, p.WorkOrderId, FieldKind.Change, ct);
        if (!loaded.IsSuccess) return loaded.Error!;
        var w = loaded.Value.WorkOrder;
        var t = WorkOrderStateMachines.WorkOrder.Resolve(w.State, command);
        if (!t.IsSuccess) return t.Error!;

        var rootCause = string.IsNullOrWhiteSpace(p.RootCauseNote) ? null : p.RootCauseNote.Trim();
        var noMaterial = string.IsNullOrWhiteSpace(p.NoMaterialReason) ? null : p.NoMaterialReason.Trim();
        if (rootCause is null) return Error.Validation("ROOT_CAUSE_REQUIRED", "Write what caused the fault.");
        if (rootCause.Length > 1000 || noMaterial is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "Notes are at most 1000 characters.");

        var checklist = await db.ChecklistResults.AsNoTracking().Where(c => c.WorkOrderId == w.Id && c.TemplateId == w.ChecklistTemplateId)
            .OrderByDescending(c => c.RecordedAt).FirstOrDefaultAsync(ct);
        if (checklist is null) return Error.Validation("CHECKLIST_MISSING", "Complete the test and checklist first.");
        if (checklist.Blocked) return Error.Validation("CHECKLIST_BLOCKING", "The checklist shows a safety problem (e.g. exposed wires): the light cannot be reported as repaired.");
        if (checklist.TestResult == "FAIL") return Error.Validation("TEST_FAILED", "The light did not pass the test.");

        var materials = await inventory.MovementsForWorkOrdersAsync([w.Id], ct);
        if (materials.Count == 0 && noMaterial is null)
        {
            return Error.Validation("MATERIALS_MISSING", "Record the material used, or state that none was used and why.");
        }

        // W9 (M5): every consumed core_return_required item has a core-return declaration.
        var undeclared = (await inventory.CoreReturnsForWorkOrdersAsync([w.Id], ct)).Where(c => c.State == "OPEN").ToList();
        if (undeclared.Count > 0)
        {
            return Error.Validation("CORE_RETURN_NOT_DECLARED", "Declare the failed part of each replaced item (with the part, or why it cannot be brought back).",
                new Dictionary<string, string>
                {
                    ["coreReturns"] = string.Join(",", undeclared.Select(c => c.Code)),
                    ["skus"] = string.Join(",", undeclared.Select(c => c.Sku)),
                });
        }

        var requirements = await reference.RequirementsForAsync(w.ChecklistTemplateId, ct);
        var photos = (await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, [w.Id], ct)).Where(e => e.SupersededBy is null && Usable(e.State)).ToList();
        var after = photos.Count(e => e.Kind == "AFTER");
        if (after < requirements.After)
        {
            return Error.Validation("EVIDENCE_MISSING", $"{requirements.After} after photos are required; {after} taken.",
                new Dictionary<string, string> { ["kind"] = "AFTER", ["required"] = requirements.After.ToString(CultureInfo.InvariantCulture), ["present"] = after.ToString(CultureInfo.InvariantCulture) });
        }

        var test = photos.Count(e => e.Kind == "TEST");
        if (checklist.TestMethod != TestMethods.NotTested && test < requirements.Test)
        {
            return Error.Validation("EVIDENCE_MISSING", "A photo of the test (light on) is required.",
                new Dictionary<string, string> { ["kind"] = "TEST", ["required"] = requirements.Test.ToString(CultureInfo.InvariantCulture), ["present"] = test.ToString(CultureInfo.InvariantCulture) });
        }

        var now = clock.UtcNowMicros();
        w.Submit(command, rootCause, noMaterial, now);
        var report = new JsonObject
        {
            ["submission_no"] = w.SubmissionCount,
            ["diagnosis_code"] = w.DiagnosisCode,
            ["root_cause_note"] = rootCause,
            ["no_material_reason"] = noMaterial,
            ["checklist_id"] = checklist.Id.ToString("D"),
            ["evidence_ids"] = new JsonArray(photos.Select(e => e.Id.ToString("D")).Order(StringComparer.Ordinal).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
            ["material_ids"] = new JsonArray(materials.Select(m => m.Id.ToString("D")).Order(StringComparer.Ordinal).Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()),
        };
        var submission = new WorkOrderSubmission(Ids.New(), w.Id, w.SubmissionCount, cmd.Actor.UserId!.Value, report.ToJsonString(), WorkOrderSeal.ReportHash(report), now);
        db.Submissions.Add(submission);
        audit.Record(w, command == WorkOrderCommands.Resubmit ? "work_order.resubmitted" : "work_order.submitted");
        audit.Record(submission, "work_order_submission.recorded", extra: new Dictionary<string, object?> { ["payload_hash"] = submission.PayloadHash });
        WorkOrderEventLog.Append(db, audit, w, command == WorkOrderCommands.Resubmit ? WorkOrderEventLog.Resubmitted : WorkOrderEventLog.Submitted, cmd, now,
            new Dictionary<string, string?> { ["submission_no"] = w.SubmissionCount.ToString(CultureInfo.InvariantCulture), ["payload_hash"] = submission.PayloadHash });
        return new SyncCommandOutcome(w.Id);
    }

    /// <summary>A photo still counts toward requirements unless verification proved it unusable (then it must be replaced).</summary>
    internal static bool Usable(string state) => state is not ("HASH_MISMATCH" or "DUPLICATE");

    private sealed record FieldTarget(WorkOrder WorkOrder, bool PreviousAssignee);

    /// <summary>
    /// The conflict matrix (docs/spec/10 §10.5) for a technician command that may have been made offline:
    /// never assigned to this user → NOT_ASSIGNEE; cancelled meanwhile → QUARANTINE (a supervisor decides);
    /// reassigned meanwhile → facts kept with a SUBMITTED_BY_NON_ASSIGNEE flag, changes refused (WORK_ORDER_REASSIGNED);
    /// changed by someone else since the version the phone saw → changes refused (WORK_ORDER_CHANGED), facts kept.
    /// Nothing newer on the server is ever overwritten silently.
    /// </summary>
    private async Task<Result<FieldTarget>> LoadForFieldAsync(CommandEnvelope cmd, Guid workOrderId, FieldKind kind, CancellationToken ct)
    {
        var w = await db.WorkOrders.SingleOrDefaultAsync(x => x.Id == workOrderId, ct);
        if (w is null) return Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");
        var userId = cmd.Actor.UserId!.Value;
        var isAssignee = w.AssigneeId == userId;
        if (!isAssignee && !await db.Assignments.AnyAsync(a => a.WorkOrderId == w.Id && a.AssigneeId == userId, ct))
        {
            return Error.Forbidden(NotAssignee, "This work order is not assigned to you.");
        }

        if (w.State == WorkOrderStates.Cancelled)
        {
            return SyncDispositions.QuarantineFor(WorkOrderCancelled, "The work order was cancelled while this was done; a supervisor decides.");
        }

        if (!isAssignee)
        {
            if (kind == FieldKind.Change)
            {
                return Error.Conflict(WorkOrderReassigned, "The work order was reassigned to another technician: this step is not applied (your photos and materials are kept).");
            }

            return w.State == WorkOrderStates.Closed
                ? SyncDispositions.QuarantineFor(WorkOrderClosed, "The work order was closed by another technician's work; a supervisor decides.")
                : new FieldTarget(w, true);
        }

        if (kind == FieldKind.Change && !w.IsTerminal) // a closed work order refuses every change anyway (INVALID_TRANSITION)
        {
            var stale = await ChangedSinceAsync(cmd, w, ct);
            if (stale is not null) return stale;
        }

        return new FieldTarget(w, false);
    }

    /// <summary>Events after the phone's base version made by anyone else (another user, or this user on another device).</summary>
    private async Task<Error?> ChangedSinceAsync(CommandEnvelope cmd, WorkOrder w, CancellationToken ct)
    {
        if (cmd.BaseVersion is not { } baseVersion)
        {
            return cmd.Actor.DeviceId is null ? null : Error.Validation(BaseVersionRequired, "Refresh the task and try again (the version you worked on is missing).");
        }

        if (w.LastEventSeq <= baseVersion) return null;
        var userId = cmd.Actor.UserId;
        var deviceId = cmd.Actor.DeviceId;
        var changes = await db.Events.AsNoTracking()
            .Where(e => e.WorkOrderId == w.Id && e.Seq > baseVersion && (e.ActorId != userId || e.DeviceId != deviceId))
            .OrderBy(e => e.Seq).Select(e => e.Type).ToListAsync(ct);
        if (changes.Count == 0) return null;
        return new Error(WorkOrderChanged, "The work order was changed on the server after you last refreshed it; refresh and check before repeating this step.",
            ErrorKind.Conflict, new Dictionary<string, string>
            {
                ["baseVersion"] = baseVersion.ToString(CultureInfo.InvariantCulture),
                ["serverVersion"] = w.LastEventSeq.ToString(CultureInfo.InvariantCulture),
                ["state"] = w.State,
                ["changes"] = string.Join(",", changes.Distinct()),
            });
    }

    private Task FlagPreviousAssigneeAsync(CommandEnvelope cmd, WorkOrder w, string command, Guid entityId, CancellationToken ct) =>
        flags.RaiseAsync(FlagCodes.SubmittedByNonAssignee, "WARNING", "work_order", w.Id, new Dictionary<string, object?>
        {
            ["command"] = command,
            ["entity_id"] = entityId.ToString("D"),
            ["previous_assignee_id"] = cmd.Actor.UserId?.ToString("D"),
            ["current_assignee_id"] = w.AssigneeId?.ToString("D"),
        }, ct);

    private static DateTimeOffset? Device(CommandEnvelope cmd) => cmd.DeviceTime is { } t ? ClockExtensions.TruncateToMicros(t) : null;
}

/// <summary>The phone's commands, registered with the Sync pipeline (permission per command, see permissions.yaml).</summary>
public abstract class FieldCommandHandler<TPayload>(string type, string permission) : ISyncCommandHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Type => type;

    public string Permission => permission;

    public async Task<Result<SyncCommandOutcome>> HandleAsync(CommandEnvelope command, JsonElement payload, CancellationToken ct)
    {
        TPayload? parsed;
        try
        {
            parsed = payload.Deserialize<TPayload>(Json);
        }
        catch (JsonException)
        {
            parsed = default;
        }

        return parsed is null ? Error.Validation("INVALID_PAYLOAD", $"{type} payload is not valid.") : await RunAsync(command, parsed, ct);
    }

    protected abstract Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope command, TPayload payload, CancellationToken ct);
}

public sealed class ArriveOnSiteHandler(FieldExecutionService s) : FieldCommandHandler<ArriveOnSitePayload>(WorkOrderCommands.ArriveOnSite, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, ArriveOnSitePayload p, CancellationToken ct) => s.ArriveAsync(c, p, ct);
}

public sealed class AddEvidenceHandler(FieldExecutionService s) : FieldCommandHandler<AddEvidencePayload>(WorkOrderCommands.AddEvidence, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, AddEvidencePayload p, CancellationToken ct) => s.AddEvidenceAsync(c, p, ct);
}

public sealed class RecordDiagnosisHandler(FieldExecutionService s) : FieldCommandHandler<RecordDiagnosisPayload>(WorkOrderCommands.RecordDiagnosis, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, RecordDiagnosisPayload p, CancellationToken ct) => s.RecordDiagnosisAsync(c, p, ct);
}

public sealed class StartWorkHandler(FieldExecutionService s) : FieldCommandHandler<StartWorkPayload>(WorkOrderCommands.StartWork, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, StartWorkPayload p, CancellationToken ct) => s.StartWorkAsync(c, p, ct);
}

public sealed class ConsumeMaterialHandler(FieldExecutionService s) : FieldCommandHandler<ConsumeMaterialPayload>(WorkOrderCommands.ConsumeMaterial, "material.consume")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, ConsumeMaterialPayload p, CancellationToken ct) => s.ConsumeMaterialAsync(c, p, ct);
}

public sealed class DeclareCoreReturnHandler(FieldExecutionService s) : FieldCommandHandler<DeclareCoreReturnPayload>(WorkOrderCommands.DeclareCoreReturn, "core_return.declare")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, DeclareCoreReturnPayload p, CancellationToken ct) => s.DeclareCoreReturnAsync(c, p, ct);
}

public sealed class RecordChecklistHandler(FieldExecutionService s) : FieldCommandHandler<RecordChecklistPayload>(WorkOrderCommands.RecordChecklist, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, RecordChecklistPayload p, CancellationToken ct) => s.RecordChecklistAsync(c, p, ct);
}

public sealed class SubmitWorkOrderHandler(FieldExecutionService s) : FieldCommandHandler<SubmitWorkOrderPayload>(WorkOrderCommands.SubmitWorkOrder, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, SubmitWorkOrderPayload p, CancellationToken ct) =>
        s.SubmitAsync(c, WorkOrderCommands.SubmitWorkOrder, p, ct);
}

public sealed class ResubmitHandler(FieldExecutionService s) : FieldCommandHandler<SubmitWorkOrderPayload>(WorkOrderCommands.Resubmit, "workorder.execute")
{
    protected override Task<Result<SyncCommandOutcome>> RunAsync(CommandEnvelope c, SubmitWorkOrderPayload p, CancellationToken ct) =>
        s.SubmitAsync(c, WorkOrderCommands.Resubmit, p, ct);
}
