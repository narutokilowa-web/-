using System.Text.Json;
using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Audit.Contracts;
using Mfop.Evidence.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Inventory.Contracts;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.Rules.Contracts;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Application;

public sealed class WorkOrderQueries(
    WorkOrdersDbContext db,
    IUserDirectory users,
    IAssetDirectory assets,
    IFaultWorkflow faults,
    IAuditQueries auditQueries,
    IEvidenceRegistry evidence,
    IInventoryLedger inventory,
    IFlags flags,
    WorkOrderReferenceData reference,
    ReviewService reviews,
    WorkOrderRelatedEntities related) : IWorkOrderTasks
{
    public const string NotAssignee = "NOT_ASSIGNEE";

    public async Task<IReadOnlyList<WorkOrderListItem>> ListAsync(string? state, CancellationToken ct)
    {
        var query = db.WorkOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(w => w.State == state);
        var list = await query.OrderByDescending(w => w.CreatedAt).Take(500).ToListAsync(ct);
        var assetMap = await assets.GetManyAsync(list.Select(w => w.AssetId).Distinct().ToList(), ct);
        var faultMap = await faults.GetManyAsync(list.Select(w => w.FaultId).Distinct().ToList(), ct);
        var names = await users.DisplayNamesAsync(list.Where(w => w.AssigneeId is not null).Select(w => w.AssigneeId!.Value), ct);
        return list.Select(w => new WorkOrderListItem(
            w.Id, w.Code, w.State, w.Priority, w.AssetId, assetMap.GetValueOrDefault(w.AssetId)?.AssetCode, assetMap.GetValueOrDefault(w.AssetId)?.AddressText,
            faultMap.GetValueOrDefault(w.FaultId)?.Category ?? "?", w.AssigneeId, w.AssigneeId is { } a ? names.GetValueOrDefault(a, "?") : null,
            w.CreatedAt, w.SlaDueAt)).ToList();
    }

    /// <summary>
    /// docs/lean-pilot/05 §5.6 step 7: open work orders whose assignee is no longer ACTIVE (offboarded). Computed from the
    /// live user status, so the queue empties as the supervisor reassigns or cancels them.
    /// </summary>
    public async Task<IReadOnlyList<WorkOrderListItem>> NeedsReassignmentAsync(CancellationToken ct)
    {
        var open = await db.WorkOrders.AsNoTracking()
            .Where(w => w.AssigneeId != null && w.State != WorkOrderStates.Closed && w.State != WorkOrderStates.Cancelled)
            .Select(w => new { w.Id, AssigneeId = w.AssigneeId!.Value }).ToListAsync(ct);
        var inactive = await users.InactiveAsync(open.Select(w => w.AssigneeId), ct);
        var ids = open.Where(w => inactive.Contains(w.AssigneeId)).Select(w => w.Id).ToHashSet();
        return (await ListAsync(null, ct)).Where(w => ids.Contains(w.Id)).ToList();
    }

    public async Task<Result<WorkOrderDto>> GetAsync(Guid id, ActorContext actor, CancellationToken ct)
    {
        var w = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");

        var assignments = await db.Assignments.AsNoTracking().Where(a => a.WorkOrderId == id).OrderBy(a => a.StartedAt).ToListAsync(ct);
        var fault = await faults.GetAsync(w.FaultId, ct);
        var names = await users.DisplayNamesAsync(
            assignments.SelectMany(a => new[] { a.AssigneeId, a.StartedBy }).Append(w.CreatedBy).Append(w.AssigneeId ?? Guid.Empty), ct);
        return new WorkOrderDto(
            w.Id, w.Code, w.State, w.Priority, w.SlaDueAt, w.Note, w.FaultId, fault?.Category ?? "?", fault?.State ?? "?",
            await assets.GetAsync(w.AssetId, ct), w.AssigneeId, w.AssigneeId is { } a ? names.GetValueOrDefault(a, "?") : null,
            names.GetValueOrDefault(w.CreatedBy, "?"), w.CreatedAt, w.CancelReason, w.CancelledAt,
            assignments.Select(x => new AssignmentDto(x.Id, x.AssigneeId, names.GetValueOrDefault(x.AssigneeId, "?"), x.State, x.StartedAt,
                names.GetValueOrDefault(x.StartedBy, "?"), x.EndedAt, x.EndReason)).ToList(),
            AllowedCommands(w, actor));
    }

    /// <summary>
    /// Timeline of the work order from the audit log: the work order and everything attached to it (assignments, events,
    /// arrivals, evidence, material movements, checklists, submissions, reviews, flags, the fault and its confirmations).
    /// Every row carries its audit event's actor, device, time, reason, old → new values and hash.
    /// </summary>
    public async Task<Result<IReadOnlyList<HistoryEntry>>> TimelineAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");
        var keys = await related.KeysAsync([w], ct);
        keys.Add(("fault", w.FaultId.ToString("D")));
        var events = await auditQueries.ForEntitiesAsync(keys, ct);
        var names = await users.DisplayNamesAsync(events.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value), ct);
        return events.Select(e => new HistoryEntry(
            e.Seq, e.OccurredAt, e.ActorUserId, e.ActorUserId is { } u ? names.GetValueOrDefault(u, "?") : "SYSTEM",
            e.ActorRoles, e.Action, e.EntityType, e.EntityId, e.Changes, e.Facts, e.Reason, e.Hash, e.DeviceId, e.CommandId)).ToList();
    }

    public async Task<IReadOnlyList<WorkOrderTask>> OpenTasksForAsync(Guid assigneeId, CancellationToken ct)
    {
        var list = await db.WorkOrders.AsNoTracking()
            .Where(w => w.AssigneeId == assigneeId && w.State != WorkOrderStates.Closed && w.State != WorkOrderStates.Cancelled)
            .ToListAsync(ct);
        return await ToTasksAsync(list, ct);
    }

    /// <summary>N5: a technician can open only a work order currently assigned to them.</summary>
    public async Task<Result<WorkOrderTask>> TaskForAssigneeAsync(Guid id, ActorContext actor, CancellationToken ct)
    {
        var w = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");
        if (w.AssigneeId != actor.UserId || w.IsTerminal)
        {
            return Error.Forbidden(NotAssignee, "This work order is not assigned to you.");
        }

        return (await ToTasksAsync([w], ct))[0];
    }

    /// <summary>The supervisor's review screen (09 §9.3): photos, diagnosis, material with cost, checklist, arrivals, submissions, flags.</summary>
    public async Task<Result<WorkOrderReviewDto>> ReviewAsync(Guid id, ActorContext actor, CancellationToken ct)
    {
        var w = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");
        var arrivals = await db.Arrivals.AsNoTracking().Where(a => a.WorkOrderId == id).OrderBy(a => a.ArrivalNo).ToListAsync(ct);
        var photos = await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, [id], ct);
        var materials = await inventory.MovementsForWorkOrdersAsync([id], ct);
        var checklist = await db.ChecklistResults.AsNoTracking().Where(c => c.WorkOrderId == id).OrderByDescending(c => c.RecordedAt).FirstOrDefaultAsync(ct);
        var submissions = await db.Submissions.AsNoTracking().Where(s => s.WorkOrderId == id).OrderBy(s => s.SubmissionNo).ToListAsync(ct);
        var decisions = await db.Reviews.AsNoTracking().Where(r => r.WorkOrderId == id).OrderBy(r => r.DecidedAt).ToListAsync(ct);
        var woFlags = await flags.ForSubjectsAsync("work_order", [id], ct);
        var diagnosis = w.DiagnosisCode is null ? null : await db.DiagnosisCodes.AsNoTracking().SingleOrDefaultAsync(d => d.Code == w.DiagnosisCode, ct);
        var template = w.ChecklistTemplateId is { } tid ? await reference.TemplateAsync(tid, ct) : null;
        var executors = await reviews.ExecutorsAsync(w, ct);

        var names = await users.DisplayNamesAsync(
            arrivals.Select(a => a.UserId).Concat(photos.Select(p => p.CapturedBy)).Concat(submissions.Select(s => s.SubmittedBy))
                .Concat(decisions.Select(d => d.ReviewerId)).Concat(executors).Append(checklist?.RecordedBy ?? Guid.Empty).Append(w.ClosedBy ?? Guid.Empty), ct);
        string Name(Guid g) => names.GetValueOrDefault(g, "?");

        var pending = photos.Count(p => p.SupersededBy is null && p.State is "ANNOUNCED" or "UPLOADED");
        var blocked = photos.Count(p => p.SupersededBy is null && p.State is "HASH_MISMATCH" or "DUPLICATE");
        string? approveBlocked = w.State != WorkOrderStates.PendingReview ? "NOT_PENDING_REVIEW"
            : actor.UserId is { } me && executors.Contains(me) ? "SOD_VIOLATION"
            : pending > 0 ? "EVIDENCE_NOT_VERIFIED"
            : blocked > 0 ? "EVIDENCE_UNUSABLE"
            : null;

        var expectedMissing = diagnosis is null ? [] : diagnosis.ExpectedItems.Where(sku => materials.All(m => m.Sku != sku)).ToList();
        var submissionDtos = new List<SubmissionDto>();
        IReadOnlyList<Guid> previousEvidence = [], previousMaterials = [];
        foreach (var s in submissions)
        {
            using var report = JsonDocument.Parse(s.Report);
            var r = report.RootElement;
            var evidenceIds = r.GetProperty("evidence_ids").EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).ToList();
            var materialIds = r.GetProperty("material_ids").EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).ToList();
            submissionDtos.Add(new SubmissionDto(s.Id, s.SubmissionNo, Name(s.SubmittedBy), s.SubmittedAt, s.PayloadHash, evidenceIds, materialIds,
                r.TryGetProperty("checklist_id", out var c) && c.ValueKind == JsonValueKind.String ? Guid.Parse(c.GetString()!) : null,
                r.TryGetProperty("root_cause_note", out var rc) ? rc.GetString() : null,
                r.TryGetProperty("no_material_reason", out var nm) ? nm.GetString() : null,
                evidenceIds.Except(previousEvidence).ToList(), materialIds.Except(previousMaterials).ToList()));
            previousEvidence = evidenceIds;
            previousMaterials = materialIds;
        }

        var bySubmission = submissions.ToDictionary(s => s.Id, s => s.SubmissionNo);
        int? workMinutes = w.WorkStartedAt is { } start && w.SubmittedAt is { } end ? (int)Math.Round((end - start).TotalMinutes) : null;
        return new WorkOrderReviewDto(
            w.Id, w.Code, w.State, diagnosis is null ? null : WorkOrderReferenceData.ToDto(diagnosis), w.DiagnosisNote, w.RootCauseNote, w.NoMaterialReason,
            w.TestMethod, w.TestResult, w.FirstArrivedAt, w.WorkStartedAt, w.SubmittedAt, workMinutes,
            arrivals.Select(a => new ArrivalDto(a.Id, a.ArrivalNo, a.UserId, Name(a.UserId), a.TagScanned, a.ScannedCode, a.ManualCode, a.ManualReason, a.DeviceTime, a.RecordedAt)).ToList(),
            photos.Select(p => new ReviewEvidenceDto(p.Id, p.Kind, p.State, p.Sha256Device, p.Sha256Server, p.CapturedAt, p.AnnouncedAt, p.VerifiedAt, p.HasPreview,
                p.DuplicateOf, p.SupersededBy, p.SupersedeReason, Name(p.CapturedBy))).ToList(),
            materials, expectedMissing, template,
            checklist is null ? null : new ChecklistResultDto(checklist.Id, checklist.TemplateId, checklist.TestMethod, checklist.NotTestedReason, checklist.TestResult,
                checklist.Blocked, JsonSerializer.Deserialize<Dictionary<string, string>>(checklist.Answers)!, Name(checklist.RecordedBy), checklist.RecordedAt),
            submissionDtos,
            decisions.Select(d => new ReviewDecisionDto(d.Id, bySubmission.GetValueOrDefault(d.SubmissionId), Name(d.ReviewerId), d.Decision, d.Reasons, d.Note, d.DecidedAt)).ToList(),
            woFlags.Select(f => new ReviewFlagDto(f.Id, f.RuleCode, f.Severity, f.State, f.Explanation, f.CreatedAt)).ToList(),
            pending, blocked, approveBlocked is null && actor.HasRole(RoleCodes.Supervisor), approveBlocked,
            executors.Select(Name).Order(StringComparer.Ordinal).ToList(), w.SealHash, w.ClosedAt, w.ClosedBy is { } cb ? Name(cb) : null);
    }

    /// <summary>Dashboard (09 §9.5, DEMO 1 subset): counts and the recently completed work orders.</summary>
    public async Task<DashboardSummary> DashboardAsync(CancellationToken ct)
    {
        var byStatus = await assets.CountActiveByOperationalStatusAsync(ct);
        var (openFaults, withoutPower) = await faults.CountOpenAsync(ct);
        var byState = await db.WorkOrders.AsNoTracking()
            .Where(w => w.State != WorkOrderStates.Closed && w.State != WorkOrderStates.Cancelled)
            .GroupBy(w => w.State).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var recent = await db.WorkOrders.AsNoTracking().Where(w => w.State == WorkOrderStates.Closed)
            .OrderByDescending(w => w.ClosedAt).Take(10).ToListAsync(ct);
        var assetMap = await assets.GetManyAsync(recent.Select(w => w.AssetId).Distinct().ToList(), ct);
        var names = await users.DisplayNamesAsync(recent.Where(w => w.ClosedBy is not null).Select(w => w.ClosedBy!.Value), ct);
        return new DashboardSummary(
            byStatus.Select(kv => new CountByKey(kv.Key, kv.Value)).OrderBy(c => c.Key, StringComparer.Ordinal).ToList(),
            openFaults, withoutPower,
            byState.Select(x => new CountByKey(x.Key, x.Count)).OrderBy(c => c.Key, StringComparer.Ordinal).ToList(),
            byState.Where(x => x.Key == WorkOrderStates.PendingReview).Sum(x => x.Count),
            recent.Select(w => new CompletedWorkOrder(w.Id, w.Code, w.AssetId, assetMap.GetValueOrDefault(w.AssetId)?.AssetCode,
                assetMap.GetValueOrDefault(w.AssetId)?.AddressText, w.DiagnosisCode, w.ClosedAt!.Value,
                w.ClosedBy is { } c ? names.GetValueOrDefault(c, "?") : "?", w.SealHash)).ToList());
    }

    private async Task<IReadOnlyList<WorkOrderTask>> ToTasksAsync(IReadOnlyList<WorkOrder> list, CancellationToken ct)
    {
        var ids = list.Select(w => w.Id).ToList();
        var assignedAt = await db.Assignments.AsNoTracking()
            .Where(a => ids.Contains(a.WorkOrderId) && a.State == AssignmentStates.Active)
            .ToDictionaryAsync(a => a.WorkOrderId, a => a.StartedAt, ct);
        var assetMap = await assets.GetManyAsync(list.Select(w => w.AssetId).Distinct().ToList(), ct);
        var faultMap = await faults.GetManyAsync(list.Select(w => w.FaultId).Distinct().ToList(), ct);
        var photos = (await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, ids, ct)).Where(e => e.SupersededBy is null && FieldExecutionService.Usable(e.State)).ToList();
        var materials = await inventory.MovementsForWorkOrdersAsync(ids, ct);
        var arrivals = await db.Arrivals.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)).GroupBy(a => a.WorkOrderId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var checklists = await db.ChecklistResults.AsNoTracking().Where(c => ids.Contains(c.WorkOrderId)).Select(c => c.WorkOrderId).Distinct().ToListAsync(ct);
        var returns = (await db.Reviews.AsNoTracking().Where(r => ids.Contains(r.WorkOrderId) && r.Decision == "RETURN").ToListAsync(ct))
            .GroupBy(r => r.WorkOrderId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.DecidedAt).First());
        return list
            .Select(w =>
            {
                var mine = photos.Where(p => p.OwnerId == w.Id).ToList();
                var lastReturn = w.State == WorkOrderStates.Returned ? returns.GetValueOrDefault(w.Id) : null;
                var progress = new TaskProgress(w.ChecklistTemplateId, w.DiagnosisCode, w.DiagnosisNote, arrivals.GetValueOrDefault(w.Id),
                    mine.Count(p => p.Kind == "BEFORE"), mine.Count(p => p.Kind == "AFTER"), mine.Count(p => p.Kind == "TEST"),
                    materials.Count(m => m.WorkOrderId == w.Id), checklists.Contains(w.Id), w.TestMethod, w.SubmissionCount,
                    lastReturn?.Reasons ?? [], lastReturn?.Note);
                return new WorkOrderTask(
                    w.Id, w.Code, w.State, w.Priority, w.SlaDueAt, w.Note, faultMap[w.FaultId].Category, faultMap[w.FaultId].Description,
                    faultMap[w.FaultId].PowerAtInspection, assignedAt.GetValueOrDefault(w.Id, w.CreatedAt), assetMap[w.AssetId], progress, w.LastEventSeq);
            })
            .OrderBy(t => t.Priority, StringComparer.Ordinal)
            .ThenBy(t => t.SlaDueAt ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.AssignedAt)
            .ToList();
    }

    /// <summary>Supervisor commands allowed now (drives the web buttons; the server re-checks everything).</summary>
    private static IReadOnlyList<string> AllowedCommands(WorkOrder w, ActorContext actor) =>
        actor.HasRole(RoleCodes.Supervisor)
            ? WorkOrderStateMachines.WorkOrder.Transitions
                .Where(t => t.From.Contains(w.State) && t.Actors.Contains(RoleCodes.Supervisor))
                .Select(t => t.Command).Distinct().ToList()
            : [];
}

/// <summary>Adds the asset's work orders and everything attached to them to the asset history timeline.</summary>
public sealed class WorkOrderAssetHistory(WorkOrdersDbContext db, WorkOrderRelatedEntities related, IRecommendations recommendations) : IAssetRelatedHistory
{
    public async Task<IReadOnlyList<(string EntityType, string EntityId)>> RelatedAuditEntitiesAsync(Guid assetId, CancellationToken ct)
    {
        var workOrders = await db.WorkOrders.AsNoTracking().Where(w => w.AssetId == assetId).ToListAsync(ct);
        var keys = await related.KeysAsync(workOrders, ct);
        keys.AddRange((await recommendations.OpenForAssetAsync(assetId, ct)).Select(r => ("recommendation", r.Id.ToString("D"))));
        return keys;
    }
}

/// <summary>Audit keys of everything attached to work orders (no dependency on the Assets module, to keep DI acyclic).</summary>
public sealed class WorkOrderRelatedEntities(WorkOrdersDbContext db, IEvidenceRegistry evidence, IInventoryLedger inventory, IFlags flags)
{
    public async Task<List<(string, string)>> KeysAsync(IReadOnlyCollection<WorkOrder> workOrders, CancellationToken ct)
    {
        var ids = workOrders.Select(w => w.Id).ToList();
        var keys = ids.Select(x => ("work_order", x.ToString("D"))).ToList();
        async Task Add<T>(IQueryable<T> q, Func<T, Guid> id, string type) => keys.AddRange((await q.ToListAsync(ct)).Select(x => (type, id(x).ToString("D"))));
        await Add(db.Assignments.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)), a => a.Id, "work_order_assignment");
        await Add(db.Events.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)), a => a.Id, "work_order_event");
        await Add(db.Arrivals.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)), a => a.Id, "work_order_arrival");
        await Add(db.ChecklistResults.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)), a => a.Id, "checklist_result");
        await Add(db.Submissions.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)), a => a.Id, "work_order_submission");
        await Add(db.Reviews.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)), a => a.Id, "work_order_review");
        keys.AddRange((await evidence.ForOwnersAsync(EvidenceOwnerTypes.WorkOrder, ids, ct)).Select(e => ("evidence", e.Id.ToString("D"))));
        keys.AddRange((await inventory.MovementsForWorkOrdersAsync(ids, ct)).Select(m => ("stock_movement", m.Id.ToString("D"))));
        keys.AddRange((await flags.ForSubjectsAsync("work_order", ids, ct)).Select(f => ("flag", f.Id.ToString("D"))));
        return keys;
    }
}
