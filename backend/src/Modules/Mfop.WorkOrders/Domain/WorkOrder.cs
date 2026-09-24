using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;
using Mfop.Kernel.StateMachines;

namespace Mfop.WorkOrders.Domain;

public static class WorkOrderStates
{
    public const string New = "NEW", Assigned = "ASSIGNED", OnSite = "ON_SITE", InProgress = "IN_PROGRESS", PendingReview = "PENDING_REVIEW",
        Returned = "RETURNED", Closed = "CLOSED", Cancelled = "CANCELLED";

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string> { Closed, Cancelled };
}

public static class WorkOrderCommands
{
    public const string CreateWorkOrder = nameof(CreateWorkOrder);
    public const string Assign = nameof(Assign);
    public const string Reassign = nameof(Reassign);
    public const string Cancel = nameof(Cancel);
    public const string ArriveOnSite = nameof(ArriveOnSite);
    public const string StartWork = nameof(StartWork);
    public const string SubmitWorkOrder = nameof(SubmitWorkOrder);
    public const string ApproveWorkOrder = nameof(ApproveWorkOrder);
    public const string ReturnWorkOrder = nameof(ReturnWorkOrder);
    public const string Resubmit = nameof(Resubmit);
}

public static class AssignmentStates
{
    public const string Active = "ACTIVE", Ended = "ENDED";
}

public static class Priorities
{
    public static readonly IReadOnlyList<string> All = ["P1", "P2", "P3", "P4"];
}

/// <summary>
/// Transition tables. MUST equal docs/state-machines/{work-order,assignment}.yaml (StateMachineConformanceTests).
/// M2 implements the supervisor commands W1–W4; the technician commands (W5, W6, W9, W12) and review (W10, W11) come in M3,
/// but the table is complete so that every unlisted (state, command) pair is already rejected.
/// </summary>
public static class WorkOrderStateMachines
{
    private const string Start = StateMachine.Start;

    public static readonly StateMachine WorkOrder = new(
        "WorkOrder",
        [WorkOrderStates.New, WorkOrderStates.Assigned, WorkOrderStates.OnSite, WorkOrderStates.InProgress, WorkOrderStates.PendingReview,
         WorkOrderStates.Returned, WorkOrderStates.Closed, WorkOrderStates.Cancelled],
        WorkOrderStates.New,
        [WorkOrderStates.Closed, WorkOrderStates.Cancelled],
        [
            new("W1", [Start], WorkOrderCommands.CreateWorkOrder, WorkOrderStates.New, ["SUPERVISOR"]),
            new("W2", [WorkOrderStates.New], WorkOrderCommands.Assign, WorkOrderStates.Assigned, ["SUPERVISOR"]),
            new("W3", [WorkOrderStates.Assigned, WorkOrderStates.OnSite, WorkOrderStates.InProgress, WorkOrderStates.Returned], WorkOrderCommands.Reassign, WorkOrderStates.Assigned, ["SUPERVISOR"]),
            new("W4", [WorkOrderStates.New, WorkOrderStates.Assigned], WorkOrderCommands.Cancel, WorkOrderStates.Cancelled, ["SUPERVISOR"]),
            new("W5", [WorkOrderStates.Assigned, WorkOrderStates.Returned], WorkOrderCommands.ArriveOnSite, WorkOrderStates.OnSite, ["assignee"]),
            new("W6", [WorkOrderStates.OnSite], WorkOrderCommands.StartWork, WorkOrderStates.InProgress, ["assignee"]),
            new("W9", [WorkOrderStates.InProgress], WorkOrderCommands.SubmitWorkOrder, WorkOrderStates.PendingReview, ["assignee"]),
            new("W10", [WorkOrderStates.PendingReview], WorkOrderCommands.ApproveWorkOrder, WorkOrderStates.Closed, ["SUPERVISOR"]),
            new("W11", [WorkOrderStates.PendingReview], WorkOrderCommands.ReturnWorkOrder, WorkOrderStates.Returned, ["SUPERVISOR"]),
            new("W12", [WorkOrderStates.Returned], WorkOrderCommands.Resubmit, WorkOrderStates.PendingReview, ["assignee"]),
        ]);

    public static readonly StateMachine Assignment = new(
        "Assignment",
        [AssignmentStates.Active, AssignmentStates.Ended],
        AssignmentStates.Active,
        [AssignmentStates.Ended],
        [
            new("A1", [Start], "OnAssigned", AssignmentStates.Active, ["system"]),
            new("A2", [AssignmentStates.Active], "OnReassigned", AssignmentStates.Ended, ["system"]),
            new("A3", [AssignmentStates.Active], "OnWorkOrderTerminal", AssignmentStates.Ended, ["system"]),
        ]);
}

/// <summary>A work order created by a supervisor from an OPEN fault on an ACTIVE street light.</summary>
public sealed class WorkOrder : IAuditable
{
    private WorkOrder() { }

    public WorkOrder(Guid id, string code, Guid faultId, Guid assetId, string priority, DateTimeOffset? slaDueAt, Guid createdBy, DateTimeOffset createdAt, string? note)
    {
        Id = id;
        Code = code;
        FaultId = faultId;
        AssetId = assetId;
        Priority = priority;
        SlaDueAt = slaDueAt;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        Note = note;
        State = WorkOrderStateMachines.WorkOrder.Create(WorkOrderCommands.CreateWorkOrder).Value.To;
    }

    public string AuditEntityType => "work_order";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid FaultId { get; private set; }
    public Guid AssetId { get; private set; }
    public string Priority { get; private set; } = "P3";
    public string State { get; private set; } = WorkOrderStates.New;
    public Guid? AssigneeId { get; private set; }
    public DateTimeOffset? SlaDueAt { get; private set; }
    public string? Note { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? CancelReason { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public int Version { get; private set; }

    public bool IsTerminal => WorkOrderStates.Terminal.Contains(State);

    /// <summary>W2 / W3: guard checks on the assignee are done by the service (roles, user status, active device).</summary>
    public Result<Transition> AssignTo(string command, Guid assigneeId)
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, command);
        if (!t.IsSuccess) return t;
        if (command == WorkOrderCommands.Reassign && AssigneeId == assigneeId)
        {
            return Error.Validation("SAME_ASSIGNEE", "The work order is already assigned to this technician.");
        }

        State = t.Value.To;
        AssigneeId = assigneeId;
        return t;
    }

    /// <summary>W4.</summary>
    public Result<Transition> Cancel(Guid actorId, string reason, DateTimeOffset now)
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, WorkOrderCommands.Cancel);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        CancelReason = reason;
        CancelledBy = actorId;
        CancelledAt = now;
        return t;
    }
}

/// <summary>Who holds the work order. One ACTIVE assignment at a time (docs/state-machines/assignment.yaml).</summary>
public sealed class WorkOrderAssignment : IAuditable
{
    private WorkOrderAssignment() { }

    public WorkOrderAssignment(Guid id, Guid workOrderId, Guid assigneeId, Guid startedBy, DateTimeOffset startedAt)
    {
        Id = id;
        WorkOrderId = workOrderId;
        AssigneeId = assigneeId;
        StartedBy = startedBy;
        StartedAt = startedAt;
        State = WorkOrderStateMachines.Assignment.Create("OnAssigned").Value.To;
    }

    public string AuditEntityType => "work_order_assignment";

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid AssigneeId { get; private set; }
    public string State { get; private set; } = AssignmentStates.Active;
    public Guid StartedBy { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public Guid? EndedBy { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? EndReason { get; private set; }
    public int Version { get; private set; }

    /// <summary>A2 (reassigned, reason required) or A3 (work order terminal).</summary>
    public void End(string command, Guid actorId, string reason, DateTimeOffset now)
    {
        State = WorkOrderStateMachines.Assignment.Resolve(State, command).Value.To;
        EndedBy = actorId;
        EndedAt = now;
        EndReason = reason;
    }
}
