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

    // Non-transition commands (04 §4.8): allowed only in listed states, by the assignee; the state does not change.
    public const string AddEvidence = nameof(AddEvidence);
    public const string RecordDiagnosis = nameof(RecordDiagnosis);
    public const string ConsumeMaterial = nameof(ConsumeMaterial);
    public const string RecordChecklist = nameof(RecordChecklist);

    /// <summary>M5 (04 §4.8, §4.11): also accepted after close until the part is received (the core-return machine decides).</summary>
    public const string DeclareCoreReturn = nameof(DeclareCoreReturn);
}

/// <summary>A non-transition command and the states in which the assignee may issue it (docs/state-machines/work-order.yaml in_state_commands).</summary>
public sealed record InStateCommand(string Command, IReadOnlyList<string> In, string Actor);

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
/// W1–W4 are supervisor commands (M2); W5, W6, W9, W12 are technician commands and W10, W11 the review (M3).
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

    /// <summary>MUST equal the in_state_commands table of docs/state-machines/work-order.yaml (StateMachineConformanceTests).</summary>
    public static readonly IReadOnlyList<InStateCommand> InStateCommands =
    [
        new(WorkOrderCommands.AddEvidence, [WorkOrderStates.OnSite, WorkOrderStates.InProgress, WorkOrderStates.Returned], "assignee"),
        new(WorkOrderCommands.RecordDiagnosis, [WorkOrderStates.OnSite, WorkOrderStates.InProgress], "assignee"),
        new(WorkOrderCommands.ConsumeMaterial, [WorkOrderStates.InProgress, WorkOrderStates.Returned], "assignee"),
        new(WorkOrderCommands.RecordChecklist, [WorkOrderStates.InProgress, WorkOrderStates.Returned], "assignee"),
        new(WorkOrderCommands.DeclareCoreReturn, [WorkOrderStates.InProgress, WorkOrderStates.Returned], "assignee"),
    ];

    /// <summary>Non-transition command guard: INVALID_TRANSITION outside its states (the state does not change).</summary>
    public static Result<bool> AllowedInState(string state, string command)
    {
        var entry = InStateCommands.Single(c => c.Command == command);
        return entry.In.Contains(state) ? true : Error.InvalidTransition("WorkOrder", state, command);
    }

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

    /// <summary>Checklist template version pinned at creation (W1) from the light's power source.</summary>
    public Guid? ChecklistTemplateId { get; private set; }

    /// <summary>Set when the asset had a work order closed as REPAIRED within the rework window (W1 effect).</summary>
    public Guid? ReworkOfId { get; private set; }
    public string? DiagnosisCode { get; private set; }
    public string? DiagnosisNote { get; private set; }
    public string? RootCauseNote { get; private set; }

    /// <summary>The explicit "no materials" statement of the submission (W9), with its justification.</summary>
    public string? NoMaterialReason { get; private set; }

    /// <summary>NIGHT, PHOTOCELL_COVERED, BYPASS or NOT_TESTED (from the latest checklist).</summary>
    public string? TestMethod { get; private set; }

    /// <summary>PASS or NOT_TESTED.</summary>
    public string? TestResult { get; private set; }

    /// <summary>REPAIRED in DEMO 1 (NO_WORK arrives with ResolveNoWork, M6).</summary>
    public string? Outcome { get; private set; }
    public DateTimeOffset? FirstArrivedAt { get; private set; }
    public DateTimeOffset? WorkStartedAt { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }

    /// <summary>Work-order seal (docs/lean-pilot/06 §6.1): SHA-256 over the canonical record of everything that happened.</summary>
    public string? SealHash { get; private set; }
    public int LastEventSeq { get; private set; }
    public int SubmissionCount { get; private set; }
    public int Version { get; private set; }

    public bool IsTerminal => WorkOrderStates.Terminal.Contains(State);

    public void PinChecklistTemplate(Guid templateId) => ChecklistTemplateId ??= templateId;

    public void MarkReworkOf(Guid previousWorkOrderId) => ReworkOfId = previousWorkOrderId;

    /// <summary>Next per-work-order event number (wo_events.seq). Touching the row also serializes concurrent commands.</summary>
    public int NextEventSeq() => ++LastEventSeq;

    /// <summary>W5.</summary>
    public Result<Transition> Arrive(DateTimeOffset now)
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, WorkOrderCommands.ArriveOnSite);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        FirstArrivedAt ??= now;
        return t;
    }

    /// <summary>Non-transition command (ON_SITE, IN_PROGRESS).</summary>
    public Result<bool> RecordDiagnosis(string code, string? note)
    {
        var allowed = WorkOrderStateMachines.AllowedInState(State, WorkOrderCommands.RecordDiagnosis);
        if (!allowed.IsSuccess) return allowed;
        DiagnosisCode = code;
        DiagnosisNote = note;
        return true;
    }

    /// <summary>W6. Guards (diagnosis, before photos) are checked by the service.</summary>
    public Result<Transition> StartWork(DateTimeOffset now)
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, WorkOrderCommands.StartWork);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        WorkStartedAt ??= now;
        return t;
    }

    public void RecordTest(string method, string result)
    {
        TestMethod = method;
        TestResult = result;
    }

    /// <summary>W9 (first submission) or W12 (resubmission after RETURNED). Guards are checked by the service.</summary>
    public Result<Transition> Submit(string command, string rootCauseNote, string? noMaterialReason, DateTimeOffset now)
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, command);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        RootCauseNote = rootCauseNote;
        NoMaterialReason = noMaterialReason;
        Outcome = "REPAIRED";
        SubmittedAt = now;
        SubmissionCount++;
        return t;
    }

    /// <summary>W10: closed and sealed. Nothing changes a CLOSED work order afterwards.</summary>
    public Result<Transition> Close(Guid reviewerId, DateTimeOffset now)
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, WorkOrderCommands.ApproveWorkOrder);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        ClosedAt = now;
        ClosedBy = reviewerId;
        return t;
    }

    public void Seal(string sealHash) => SealHash = sealHash;

    /// <summary>W11.</summary>
    public Result<Transition> ReturnForRework()
    {
        var t = WorkOrderStateMachines.WorkOrder.Resolve(State, WorkOrderCommands.ReturnWorkOrder);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        return t;
    }

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
