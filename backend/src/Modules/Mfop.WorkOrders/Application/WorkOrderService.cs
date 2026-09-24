using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mfop.WorkOrders.Application;

/// <summary>Supervisor work-order commands W1–W4 (docs/state-machines/work-order.yaml), each in one unit of work with its audit events.</summary>
public sealed class WorkOrderService(
    WorkOrdersDbContext db,
    IUnitOfWork uow,
    IAuditTrail audit,
    TimeProvider clock,
    IAssetDirectory assets,
    IFaultWorkflow faults,
    IAssigneeDirectory assignees,
    IEnumerable<IWorkOrderActivity> activity,
    IOptions<WorkOrderOptions> options)
{
    /// <summary>W1: from an OPEN fault on an ACTIVE asset. Effect F4: the fault moves to IN_WORK_ORDER.</summary>
    public Task<Result<Guid>> CreateAsync(CommandEnvelope cmd, CreateWorkOrderRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (request.Priority is null || !Priorities.All.Contains(request.Priority))
            {
                return Error.Validation("INVALID_PRIORITY", "Priority must be P1, P2, P3 or P4.");
            }

            if (request.Note is { Length: > 1000 }) return Error.Validation("NOTE_TOO_LONG", "The note is at most 1000 characters.");

            var fault = await faults.GetAsync(request.FaultId, innerCt);
            if (fault is null) return Error.NotFound("FAULT_NOT_FOUND", "Fault not found.");

            // N15: no work order for an asset that is not ACTIVE (e.g. PENDING, later UNDER_INVESTIGATION).
            var asset = await assets.GetAsync(fault.AssetId, innerCt);
            if (asset is null || asset.LifecycleState != AssetLifecycle.Active)
            {
                return Error.Validation("ASSET_NOT_ACTIVE", "A work order can only be created for an ACTIVE asset.");
            }

            if (await db.WorkOrders.AnyAsync(w => w.FaultId == fault.Id && w.State != WorkOrderStates.Closed && w.State != WorkOrderStates.Cancelled, innerCt))
            {
                return Error.Conflict("WORK_ORDER_EXISTS", "This fault already has an open work order.");
            }

            var now = clock.UtcNowMicros();
            var number = await db.Database.SqlQuery<long>($"SELECT nextval('workorders.work_order_code_seq') AS \"Value\"").SingleAsync(innerCt);
            var code = $"WO-{YearInOrganizationTimeZone(now)}-{number:D6}";
            DateTimeOffset? slaDue = options.Value.SlaHoursByPriority.TryGetValue(request.Priority, out var hours) && hours > 0 ? now.AddHours(hours) : null;
            var workOrder = new WorkOrder(Ids.New(), code, fault.Id, asset.Id, request.Priority, slaDue, cmd.Actor.UserId!.Value, now,
                string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());
            db.WorkOrders.Add(workOrder);
            audit.Record(workOrder, "work_order.created");

            var faultMoved = await faults.OnWorkOrderCreatedAsync(fault.Id, workOrder.Id, innerCt);
            if (!faultMoved.IsSuccess) return faultMoved.Error!;
            return workOrder.Id;
        }, ct);

    /// <summary>W2: to an active technician who has an ACTIVE device.</summary>
    public Task<Result<Guid>> AssignAsync(CommandEnvelope cmd, Guid workOrderId, AssignRequest request, CancellationToken ct) =>
        AssignOrReassignAsync(cmd, workOrderId, request, WorkOrderCommands.Assign, ct);

    /// <summary>W3: reason required; the previous assignment ends (A2) and the old assignee loses access.</summary>
    public Task<Result<Guid>> ReassignAsync(CommandEnvelope cmd, Guid workOrderId, AssignRequest request, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(request.Reason)
            ? Task.FromResult(Result<Guid>.Failure(Error.Validation("REASON_REQUIRED", "A reason is required to reassign.")))
            : AssignOrReassignAsync(cmd, workOrderId, request, WorkOrderCommands.Reassign, ct);

    /// <summary>W4: only while nothing was done on site (no evidence, no consumption). Effect F5: the fault is OPEN again.</summary>
    public Task<Result<Guid>> CancelAsync(CommandEnvelope cmd, Guid workOrderId, CancelRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason)) return Error.Validation("REASON_REQUIRED", "A reason is required to cancel.");
            var workOrder = await db.WorkOrders.SingleOrDefaultAsync(w => w.Id == workOrderId, innerCt);
            if (workOrder is null) return NotFound();

            foreach (var probe in activity)
            {
                if (await probe.HasEvidenceOrConsumptionAsync(workOrder.Id, innerCt))
                {
                    return Error.InvalidTransition("WorkOrder", workOrder.State, WorkOrderCommands.Cancel);
                }
            }

            var now = clock.UtcNowMicros();
            var reason = request.Reason.Trim();
            var cancelled = workOrder.Cancel(cmd.Actor.UserId!.Value, reason, now);
            if (!cancelled.IsSuccess) return cancelled.Error!;
            audit.Record(workOrder, "work_order.cancelled", reason);

            foreach (var assignment in await db.Assignments.Where(a => a.WorkOrderId == workOrder.Id && a.State == AssignmentStates.Active).ToListAsync(innerCt))
            {
                assignment.End("OnWorkOrderTerminal", cmd.Actor.UserId!.Value, "WORK_ORDER_CANCELLED", now);
                audit.Record(assignment, "work_order_assignment.ended");
            }

            var faultMoved = await faults.OnWorkOrderCancelledAsync(workOrder.FaultId, innerCt);
            if (!faultMoved.IsSuccess) return faultMoved.Error!;
            return workOrder.Id;
        }, ct);

    private Task<Result<Guid>> AssignOrReassignAsync(CommandEnvelope cmd, Guid workOrderId, AssignRequest request, string command, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var workOrder = await db.WorkOrders.SingleOrDefaultAsync(w => w.Id == workOrderId, innerCt);
            if (workOrder is null) return NotFound();

            var check = await assignees.CheckAsync(request.AssigneeId, innerCt);
            if (!check.Exists) return Error.NotFound("ASSIGNEE_NOT_FOUND", "User not found.");
            if (!check.IsActive) return Error.Validation("ASSIGNEE_DISABLED", "This user is disabled.");
            if (!check.IsTechnician) return Error.Validation("ASSIGNEE_NOT_TECHNICIAN", "Work orders can only be assigned to technicians.");
            if (!check.HasActiveDevice) return Error.Validation("ASSIGNEE_NO_ACTIVE_DEVICE", "The technician has no approved (ACTIVE) device.");

            var assigned = workOrder.AssignTo(command, request.AssigneeId);
            if (!assigned.IsSuccess) return assigned.Error!;

            var now = clock.UtcNowMicros();
            var actorId = cmd.Actor.UserId!.Value;
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            foreach (var old in await db.Assignments.Where(a => a.WorkOrderId == workOrder.Id && a.State == AssignmentStates.Active).ToListAsync(innerCt))
            {
                old.End("OnReassigned", actorId, reason ?? "REASSIGNED", now);
                audit.Record(old, "work_order_assignment.ended", reason);
            }

            var assignment = new WorkOrderAssignment(Ids.New(), workOrder.Id, request.AssigneeId, actorId, now);
            db.Assignments.Add(assignment);
            audit.Record(workOrder, command == WorkOrderCommands.Assign ? "work_order.assigned" : "work_order.reassigned", reason);
            audit.Record(assignment, "work_order_assignment.started");
            return workOrder.Id;
        }, ct);

    private int YearInOrganizationTimeZone(DateTimeOffset now) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(options.Value.TimeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(now, zone).Year
            : throw new InvalidOperationException($"Time zone '{options.Value.TimeZone}' (WorkOrders:TimeZone) is not available on this host.");

    private static Error NotFound() => Error.NotFound("WORK_ORDER_NOT_FOUND", "Work order not found.");
}
