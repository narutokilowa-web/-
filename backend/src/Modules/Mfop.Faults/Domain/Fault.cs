using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;
using Mfop.Kernel.StateMachines;

namespace Mfop.Faults.Domain;

public static class FaultStates
{
    public const string Open = "OPEN", InWorkOrder = "IN_WORK_ORDER", Resolved = "RESOLVED", Dismissed = "DISMISSED";
}

public static class FaultCommands
{
    public const string ReportFault = nameof(ReportFault);
    public const string ConfirmFault = nameof(ConfirmFault);
    public const string DismissFault = nameof(DismissFault);
    public const string OnWorkOrderCreated = nameof(OnWorkOrderCreated);
    public const string OnWorkOrderCancelled = nameof(OnWorkOrderCancelled);
    public const string OnWorkOrderClosed = nameof(OnWorkOrderClosed);
}

/// <summary>Closed lists (docs/lean-pilot/02 §2.2 and 04 §4.7).</summary>
public static class FaultVocabulary
{
    public static readonly IReadOnlySet<string> Categories = Set("LIGHT_OFF", "FLICKERING", "DAY_BURNING", "DAMAGED_POLE", "EXPOSED_WIRES", "MISSING_PARTS", "OTHER");
    public static readonly IReadOnlySet<string> PowerAtInspection = Set("CONFIRMED_AVAILABLE", "CONFIRMED_UNAVAILABLE", "UNKNOWN");
    public static readonly IReadOnlySet<string> PowerInfoSources = Set("OBSERVED_ON_SITE", "ELECTRICITY_AUTHORITY", "SCHEDULE", "RESIDENT", "OTHER");
    public static readonly IReadOnlySet<string> Sources = Set("INSPECTION", "FIELD_STAFF", "SUPERVISOR", "PHONE_CALL");
    public static readonly IReadOnlySet<string> DismissReasons = Set("DUPLICATE", "NOT_A_FAULT", "POWER_UNAVAILABLE_AT_REPORT", "OUT_OF_MUNICIPAL_RESPONSIBILITY", "OTHER");

    public const string PowerUnavailable = "CONFIRMED_UNAVAILABLE";
    public const string CategoryOther = "OTHER";

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}

/// <summary>Transition table. MUST equal docs/state-machines/fault.yaml (StateMachineConformanceTests).</summary>
public static class FaultStateMachines
{
    public static readonly StateMachine Fault = new(
        "Fault",
        [FaultStates.Open, FaultStates.InWorkOrder, FaultStates.Resolved, FaultStates.Dismissed],
        FaultStates.Open,
        [FaultStates.Resolved, FaultStates.Dismissed],
        [
            new("F1", [StateMachine.Start], FaultCommands.ReportFault, FaultStates.Open, ["SUPERVISOR"]),
            new("F2", [FaultStates.Open], FaultCommands.ConfirmFault, FaultStates.Open, ["SUPERVISOR"]),
            new("F7", [FaultStates.InWorkOrder], FaultCommands.ConfirmFault, FaultStates.InWorkOrder, ["SUPERVISOR"]),
            new("F3", [FaultStates.Open], FaultCommands.DismissFault, FaultStates.Dismissed, ["SUPERVISOR"]),
            new("F4", [FaultStates.Open], FaultCommands.OnWorkOrderCreated, FaultStates.InWorkOrder, ["system"]),
            new("F5", [FaultStates.InWorkOrder], FaultCommands.OnWorkOrderCancelled, FaultStates.Open, ["system"]),
            new("F6", [FaultStates.InWorkOrder], FaultCommands.OnWorkOrderClosed, FaultStates.Resolved, ["system"]),
        ]);
}

/// <param name="Power">CONFIRMED_AVAILABLE / CONFIRMED_UNAVAILABLE / UNKNOWN: whether public power was there at report time.</param>
public sealed record PowerObservation(string Power, string? Source, string? Note)
{
    public Error? Validate()
    {
        var details = new Dictionary<string, string>();
        if (Power is null || !FaultVocabulary.PowerAtInspection.Contains(Power)) details["powerAtInspection"] = "CONFIRMED_AVAILABLE, CONFIRMED_UNAVAILABLE or UNKNOWN";
        if (Source is not null && !FaultVocabulary.PowerInfoSources.Contains(Source)) details["powerInfoSource"] = "unknown source";
        if (Note is { Length: > 500 }) details["powerNote"] = "at most 500 characters";
        return details.Count == 0 ? null : Error.Validation("INVALID_FAULT", "Invalid power observation.", details);
    }
}

/// <summary>
/// A reported fault on an ACTIVE street light. Duplicate reports (same asset and category while OPEN or IN_WORK_ORDER)
/// become <see cref="FaultConfirmation"/>s instead of new faults. The supervisor decides; nothing is auto-dismissed.
/// </summary>
public sealed class Fault : IAuditable
{
    private Fault() { }

    public Fault(Guid id, Guid assetId, string category, string source, Guid reportedBy, DateTimeOffset reportedAt,
        PowerObservation power, string responsibleParty, string? responsibleEntityName, string? description, string captureChannel)
    {
        Id = id;
        AssetId = assetId;
        Category = category;
        Source = source;
        ReportedBy = reportedBy;
        ReportedAt = reportedAt;
        PowerAtInspection = power.Power;
        PowerInfoSource = power.Source;
        PowerNote = power.Note;
        ResponsibleParty = responsibleParty;
        ResponsibleEntityName = responsibleEntityName;
        Description = description;
        CaptureChannel = captureChannel;
        State = FaultStateMachines.Fault.Create(FaultCommands.ReportFault).Value.To;
    }

    public string AuditEntityType => "fault";

    public Guid Id { get; private set; }
    public Guid AssetId { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public Guid ReportedBy { get; private set; }
    public DateTimeOffset ReportedAt { get; private set; }
    public string PowerAtInspection { get; private set; } = string.Empty;
    public string? PowerInfoSource { get; private set; }
    public string? PowerNote { get; private set; }
    public string ResponsibleParty { get; private set; } = string.Empty;
    public string? ResponsibleEntityName { get; private set; }
    public string? Description { get; private set; }
    public string CaptureChannel { get; private set; } = "WEB";
    public string State { get; private set; } = FaultStates.Open;
    public Guid? WorkOrderId { get; private set; }
    public string? DismissReason { get; private set; }
    public string? DismissNote { get; private set; }
    public Guid? DismissedBy { get; private set; }
    public DateTimeOffset? DismissedAt { get; private set; }
    public int ConfirmationCount { get; private set; }
    public int Version { get; private set; }

    /// <summary>"Power was unavailable at report time" badge: shown, never used to auto-dismiss.</summary>
    public bool ReportedWithoutPower => PowerAtInspection == FaultVocabulary.PowerUnavailable;

    public Result<Transition> Confirm()
    {
        var t = FaultStateMachines.Fault.Resolve(State, FaultCommands.ConfirmFault);
        if (!t.IsSuccess) return t;
        ConfirmationCount++;
        return t;
    }

    public Result<Transition> Dismiss(string reason, string? note, Guid actorId, DateTimeOffset now)
    {
        if (reason is null || !FaultVocabulary.DismissReasons.Contains(reason))
        {
            return Error.Validation("INVALID_DISMISS_REASON", "Choose a dismiss reason from the list.");
        }

        if (reason == "OTHER" && string.IsNullOrWhiteSpace(note))
        {
            return Error.Validation("NOTE_REQUIRED", "Explain the reason.");
        }

        var t = FaultStateMachines.Fault.Resolve(State, FaultCommands.DismissFault);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        DismissReason = reason;
        DismissNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DismissedBy = actorId;
        DismissedAt = now;
        return t;
    }

    public Result<Transition> OnWorkOrderCreated(Guid workOrderId)
    {
        var t = FaultStateMachines.Fault.Resolve(State, FaultCommands.OnWorkOrderCreated);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        WorkOrderId = workOrderId;
        return t;
    }

    public Result<Transition> OnWorkOrderCancelled()
    {
        var t = FaultStateMachines.Fault.Resolve(State, FaultCommands.OnWorkOrderCancelled);
        if (!t.IsSuccess) return t;
        State = t.Value.To;
        WorkOrderId = null;
        return t;
    }
}

/// <summary>A duplicate report of an existing fault. Append-only.</summary>
public sealed class FaultConfirmation : IAuditable
{
    private FaultConfirmation() { }

    public FaultConfirmation(Guid id, Guid faultId, Guid confirmedBy, DateTimeOffset at, PowerObservation power, string? description, string captureChannel)
    {
        Id = id;
        FaultId = faultId;
        ConfirmedBy = confirmedBy;
        At = at;
        PowerAtInspection = power.Power;
        PowerInfoSource = power.Source;
        PowerNote = power.Note;
        Description = description;
        CaptureChannel = captureChannel;
    }

    public string AuditEntityType => "fault_confirmation";

    public Guid Id { get; private set; }
    public Guid FaultId { get; private set; }
    public Guid ConfirmedBy { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string PowerAtInspection { get; private set; } = string.Empty;
    public string? PowerInfoSource { get; private set; }
    public string? PowerNote { get; private set; }
    public string? Description { get; private set; }
    public string CaptureChannel { get; private set; } = "WEB";
}
