using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Audit.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Application;

public sealed class WorkOrderQueries(
    WorkOrdersDbContext db, IUserDirectory users, IAssetDirectory assets, IFaultWorkflow faults, IAuditQueries auditQueries) : IWorkOrderTasks
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
    /// Timeline of the work order from the audit log: the work order, its assignments, its fault and the fault's confirmations.
    /// </summary>
    public async Task<Result<IReadOnlyList<HistoryEntry>>> TimelineAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");

        var keys = new List<(string, string)> { ("work_order", id.ToString("D")), ("fault", w.FaultId.ToString("D")) };
        keys.AddRange((await db.Assignments.AsNoTracking().Where(a => a.WorkOrderId == id).Select(a => a.Id).ToListAsync(ct))
            .Select(x => ("work_order_assignment", x.ToString("D"))));
        var events = await auditQueries.ForEntitiesAsync(keys, ct);
        var names = await users.DisplayNamesAsync(events.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value), ct);
        return events.Select(e => new HistoryEntry(
            e.Seq, e.OccurredAt, e.ActorUserId, e.ActorUserId is { } u ? names.GetValueOrDefault(u, "?") : "SYSTEM",
            e.ActorRoles, e.Action, e.EntityType, e.EntityId, e.Changes, e.Facts, e.Reason, e.Hash)).ToList();
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

    private async Task<IReadOnlyList<WorkOrderTask>> ToTasksAsync(IReadOnlyList<WorkOrder> list, CancellationToken ct)
    {
        var ids = list.Select(w => w.Id).ToList();
        var assignedAt = await db.Assignments.AsNoTracking()
            .Where(a => ids.Contains(a.WorkOrderId) && a.State == AssignmentStates.Active)
            .ToDictionaryAsync(a => a.WorkOrderId, a => a.StartedAt, ct);
        var assetMap = await assets.GetManyAsync(list.Select(w => w.AssetId).Distinct().ToList(), ct);
        var faultMap = await faults.GetManyAsync(list.Select(w => w.FaultId).Distinct().ToList(), ct);
        return list
            .Select(w => new WorkOrderTask(
                w.Id, w.Code, w.State, w.Priority, w.SlaDueAt, w.Note, faultMap[w.FaultId].Category, faultMap[w.FaultId].Description,
                faultMap[w.FaultId].PowerAtInspection, assignedAt.GetValueOrDefault(w.Id, w.CreatedAt), assetMap[w.AssetId]))
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
                .Where(t => t.Command is WorkOrderCommands.Assign or WorkOrderCommands.Reassign or WorkOrderCommands.Cancel) // M2 commands
                .Select(t => t.Command).Distinct().ToList()
            : [];
}

/// <summary>Adds the asset's work orders and their assignments to the asset history timeline.</summary>
public sealed class WorkOrderAssetHistory(WorkOrdersDbContext db) : IAssetRelatedHistory
{
    public async Task<IReadOnlyList<(string EntityType, string EntityId)>> RelatedAuditEntitiesAsync(Guid assetId, CancellationToken ct)
    {
        var ids = await db.WorkOrders.AsNoTracking().Where(w => w.AssetId == assetId).Select(w => w.Id).ToListAsync(ct);
        var assignmentIds = await db.Assignments.AsNoTracking().Where(a => ids.Contains(a.WorkOrderId)).Select(a => a.Id).ToListAsync(ct);
        return ids.Select(id => ("work_order", id.ToString("D")))
            .Concat(assignmentIds.Select(id => ("work_order_assignment", id.ToString("D"))))
            .ToList();
    }
}
