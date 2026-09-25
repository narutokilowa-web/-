using System.Globalization;
using Mfop.Assets.Contracts;
using Mfop.Evidence.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Inventory.Contracts;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Rules.Contracts;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Application;

/// <summary>
/// Supervisor review (W10 approve / W11 return), reviewer ≠ executor. Approval closes and seals the work order, sets the
/// light's status from the test (OS6 / OS7), resolves the fault (F6) and evaluates the repeated-failure rule, all in one
/// unit of work with its audit events.
/// </summary>
public sealed class ReviewService(
    WorkOrdersDbContext db,
    IUnitOfWork uow,
    IAuditTrail audit,
    TimeProvider clock,
    IAssetStatusEvents assetStatus,
    IFaultWorkflow faults,
    IEvidenceRegistry evidence,
    IInventoryLedger inventory,
    IRepeatedFailureRule repeatedFailure)
{
    public Task<Result<Guid>> ApproveAsync(CommandEnvelope cmd, Guid workOrderId, ApproveRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var w = await db.WorkOrders.SingleOrDefaultAsync(x => x.Id == workOrderId, innerCt);
            if (w is null) return NotFound();
            var t = WorkOrderStateMachines.WorkOrder.Resolve(w.State, WorkOrderCommands.ApproveWorkOrder);
            if (!t.IsSuccess) return t.Error!;
            var reviewer = cmd.Actor.UserId!.Value;
            if ((await ExecutorsAsync(w, innerCt)).Contains(reviewer)) return Error.Sod("You executed this work order; another supervisor must review it.");

            var photos = await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, [w.Id], innerCt);
            var notVerified = photos.Where(e => e.SupersededBy is null && e.State != "VERIFIED").ToList();
            if (notVerified.Count > 0)
            {
                return Error.Validation("EVIDENCE_NOT_VERIFIED", $"{notVerified.Count} photo(s) are not verified yet (all evidence must be VERIFIED).",
                    new Dictionary<string, string> { ["notVerified"] = notVerified.Count.ToString(CultureInfo.InvariantCulture) });
            }

            var submission = await db.Submissions.AsNoTracking().Where(s => s.WorkOrderId == w.Id).OrderByDescending(s => s.SubmissionNo).FirstAsync(innerCt);
            var now = clock.UtcNowMicros();
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "The note is at most 1000 characters.");

            w.Close(reviewer, now);
            var review = new WorkOrderReview(Ids.New(), w.Id, submission.Id, reviewer, "APPROVE", [], note, now);
            db.Reviews.Add(review);
            audit.Record(review, "work_order_review.recorded", note);
            WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.Approved, cmd, now, new Dictionary<string, string?>
            {
                ["submission_no"] = submission.SubmissionNo.ToString(CultureInfo.InvariantCulture),
                ["note"] = note,
            });

            foreach (var assignment in await db.Assignments.Where(a => a.WorkOrderId == w.Id && a.State == AssignmentStates.Active).ToListAsync(innerCt))
            {
                assignment.End("OnWorkOrderTerminal", reviewer, "WORK_ORDER_CLOSED", now); // A3
                audit.Record(assignment, "work_order_assignment.ended");
            }

            // Asset status (04 §4.6): tested and passed → OPERATIONAL (OS6); not tested → UNKNOWN (OS7).
            await assetStatus.ApplyAsync(w.AssetId,
                w.TestResult == "PASS" ? AssetStatusCommands.OnWorkOrderClosedTestPassed : AssetStatusCommands.OnWorkOrderClosedNotTested, "WORK_ORDER", w.Id, innerCt);

            var resolved = await faults.OnWorkOrderClosedAsync(w.FaultId, w.Id, now, innerCt); // F6
            if (!resolved.IsSuccess) return resolved.Error!;

            // Seal over everything that happened on this work order (06 §6.1), stored on the row and in the audit event.
            var seal = await ComputeSealAsync(w, innerCt);
            w.Seal(seal);
            audit.Record(w, "work_order.closed", note, new Dictionary<string, object?> { ["seal_hash"] = seal, ["seal_version"] = WorkOrderSeal.SealVersion });

            // The one DEMO 1 rule, evaluated when the fault becomes RESOLVED.
            await repeatedFailure.EvaluateAsync(w.AssetId, await ResolvedFailuresAsync(w, innerCt), innerCt);
            return w.Id;
        }, ct);

    public Task<Result<Guid>> ReturnAsync(CommandEnvelope cmd, Guid workOrderId, ReturnRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var w = await db.WorkOrders.SingleOrDefaultAsync(x => x.Id == workOrderId, innerCt);
            if (w is null) return NotFound();
            var t = WorkOrderStateMachines.WorkOrder.Resolve(w.State, WorkOrderCommands.ReturnWorkOrder);
            if (!t.IsSuccess) return t.Error!;
            var reviewer = cmd.Actor.UserId!.Value;
            if ((await ExecutorsAsync(w, innerCt)).Contains(reviewer)) return Error.Sod("You executed this work order; another supervisor must review it.");

            var reasons = (request.Reasons ?? []).Distinct(StringComparer.Ordinal).ToArray();
            if (reasons.Length == 0) return Error.Validation("RETURN_REASONS_REQUIRED", "Tick what is missing or wrong.");
            if (reasons.Any(r => !ReturnReasons.All.Contains(r))) return Error.Validation("INVALID_RETURN_REASON", "Unknown return reason.");
            var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (reasons.Contains("OTHER") && note is null) return Error.Validation("NOTE_REQUIRED", "Explain the reason.");
            if (note is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "The note is at most 1000 characters.");

            var submission = await db.Submissions.AsNoTracking().Where(s => s.WorkOrderId == w.Id).OrderByDescending(s => s.SubmissionNo).FirstAsync(innerCt);
            var now = clock.UtcNowMicros();
            w.ReturnForRework();
            var review = new WorkOrderReview(Ids.New(), w.Id, submission.Id, reviewer, "RETURN", reasons, note, now);
            db.Reviews.Add(review);
            audit.Record(w, "work_order.returned", note);
            audit.Record(review, "work_order_review.recorded", note);
            WorkOrderEventLog.Append(db, audit, w, WorkOrderEventLog.Returned, cmd, now, new Dictionary<string, string?>
            {
                ["submission_no"] = submission.SubmissionNo.ToString(CultureInfo.InvariantCulture),
                ["reasons"] = string.Join(",", reasons),
                ["note"] = note,
            });
            return w.Id;
        }, ct);

    /// <summary>reviewer_not_executor: the current and every previous assignee, anyone who acted as the technician, and every evidence author.</summary>
    public async Task<HashSet<Guid>> ExecutorsAsync(WorkOrder w, CancellationToken ct)
    {
        var executors = new HashSet<Guid>();
        if (w.AssigneeId is { } a) executors.Add(a);
        executors.UnionWith(await db.Assignments.AsNoTracking().Where(x => x.WorkOrderId == w.Id).Select(x => x.AssigneeId).ToListAsync(ct));
        var technicianEvents = new[]
        {
            WorkOrderEventLog.Arrived, WorkOrderEventLog.EvidenceAdded, WorkOrderEventLog.DiagnosisRecorded, WorkOrderEventLog.WorkStarted,
            WorkOrderEventLog.MaterialConsumed, WorkOrderEventLog.ChecklistRecorded, WorkOrderEventLog.Submitted, WorkOrderEventLog.Resubmitted,
        };
        executors.UnionWith((await db.Events.AsNoTracking().Where(e => e.WorkOrderId == w.Id && technicianEvents.Contains(e.Type) && e.ActorId != null)
            .Select(e => e.ActorId!.Value).ToListAsync(ct)));
        executors.UnionWith((await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, [w.Id], ct)).Select(e => e.CapturedBy));
        return executors;
    }

    /// <summary>Builds the seal from what is stored plus what this unit of work is adding (the approval review and event).</summary>
    private async Task<string> ComputeSealAsync(WorkOrder w, CancellationToken ct)
    {
        var events = (await db.Events.AsNoTracking().Where(e => e.WorkOrderId == w.Id).ToListAsync(ct))
            .Concat(db.Events.Local.Where(e => e.WorkOrderId == w.Id && db.Entry(e).State == EntityState.Added));
        var reviews = (await db.Reviews.AsNoTracking().Where(r => r.WorkOrderId == w.Id).ToListAsync(ct))
            .Concat(db.Reviews.Local.Where(r => r.WorkOrderId == w.Id && db.Entry(r).State == EntityState.Added));
        var arrivals = await db.Arrivals.AsNoTracking().Where(a => a.WorkOrderId == w.Id).ToListAsync(ct);
        var checklists = await db.ChecklistResults.AsNoTracking().Where(c => c.WorkOrderId == w.Id).ToListAsync(ct);
        var submissions = await db.Submissions.AsNoTracking().Where(s => s.WorkOrderId == w.Id).ToListAsync(ct);
        var photos = (await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, [w.Id], ct))
            .Select(e => new SealEvidence(e.Id, e.Kind, e.Sha256Server, e.State, e.SupersededBy));
        var materials = (await inventory.MovementsForWorkOrdersAsync([w.Id], ct))
            .Select(m => new SealMaterial(m.Id, m.Type, m.ItemId, m.Quantity, m.Serials, m.FromLocationId, m.ToLocationId, m.OccurredAt));
        return WorkOrderSeal.Hash(WorkOrderSeal.Document(w, events, arrivals, photos, materials, checklists, submissions, reviews));
    }

    /// <summary>Input of the repeated-failure rule: every work order of the light closed as REPAIRED (this one included), with its diagnosis.</summary>
    private async Task<IReadOnlyList<ResolvedFailure>> ResolvedFailuresAsync(WorkOrder current, CancellationToken ct)
    {
        var closed = await db.WorkOrders.AsNoTracking()
            .Where(x => x.AssetId == current.AssetId && x.State == WorkOrderStates.Closed && x.Outcome == "REPAIRED" && x.Id != current.Id)
            .ToListAsync(ct);
        closed.Add(current);
        var codes = await db.DiagnosisCodes.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.CountsAsFailure, ct);
        return closed.Where(x => x.DiagnosisCode is not null && x.ClosedAt is not null)
            .Select(x => new ResolvedFailure(x.FaultId, x.Id, x.Code, x.DiagnosisCode!, codes.GetValueOrDefault(x.DiagnosisCode!), x.ClosedAt!.Value))
            .ToList();
    }

    private static Error NotFound() => Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");
}
