using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;
using Mfop.Kernel.StateMachines;

namespace Mfop.Inventory.Domain;

public static class StockIssueStates
{
    public const string Issued = "ISSUED", Acknowledged = "ACKNOWLEDGED", Disputed = "DISPUTED", Cancelled = "CANCELLED";
}

public static class StockReturnStates
{
    public const string Declared = "DECLARED", Received = "RECEIVED", Rejected = "REJECTED";
}

public static class StockReturnKinds
{
    /// <summary>Unused material declared by the technician (two-party: declared, then received or rejected).</summary>
    public const string Unused = "UNUSED";

    /// <summary>Material handed back by a disabled (offboarded) technician, recorded by the warehouse (the technician cannot act).</summary>
    public const string Offboarding = "OFFBOARDING";
}

public static class CoreReturnStates
{
    public const string Open = "OPEN", DeclaredWithPart = "DECLARED_WITH_PART", DeclaredUnrecoverable = "DECLARED_UNRECOVERABLE",
        Received = "RECEIVED", Waived = "WAIVED";
}

public static class CoreReturnOutcomes
{
    public const string WithPart = "WITH_PART", Unrecoverable = "UNRECOVERABLE", Received = "RECEIVED", NotPresented = "NOT_PRESENTED";
}

public static class DocumentCommands
{
    public const string IssueStock = nameof(IssueStock);
    public const string AcknowledgeStockIssue = nameof(AcknowledgeStockIssue);
    public const string DisputeStockIssue = nameof(DisputeStockIssue);
    public const string CancelStockIssue = nameof(CancelStockIssue);

    public const string DeclareStockReturn = nameof(DeclareStockReturn);
    public const string ReceiveStockReturn = nameof(ReceiveStockReturn);
    public const string RejectStockReturn = nameof(RejectStockReturn);
    public const string ReceiveOffboardingReturn = nameof(ReceiveOffboardingReturn);

    /// <summary>The obligation opens when a core_return_required item is consumed on a work order.</summary>
    public const string ConsumeMaterial = nameof(ConsumeMaterial);

    /// <summary>
    /// The phone sends one command, DeclareCoreReturn, with an outcome; the table has one row per outcome because a
    /// (state, command) pair must lead to exactly one state.
    /// </summary>
    public const string DeclareCoreReturn = nameof(DeclareCoreReturn);
    public const string DeclareCoreReturnWithPart = nameof(DeclareCoreReturnWithPart);
    public const string DeclareCoreReturnUnrecoverable = nameof(DeclareCoreReturnUnrecoverable);

    /// <summary>The warehouse sends ReceiveCorePart with outcome RECEIVED or NOT_PRESENTED (one row per outcome, as above).</summary>
    public const string ReceiveCorePart = nameof(ReceiveCorePart);
    public const string RecordCorePartNotPresented = nameof(RecordCorePartNotPresented);
    public const string WaiveCoreReturn = nameof(WaiveCoreReturn);
}

/// <summary>M5 document machines. Each MUST equal its docs/state-machines/*.yaml table (StateMachineConformanceTests).</summary>
public static class StockDocumentMachines
{
    /// <summary>docs/lean-pilot/04 §4.9 (stock-issue.yaml).</summary>
    public static readonly StateMachine StockIssue = new(
        "StockIssue",
        [StockIssueStates.Issued, StockIssueStates.Acknowledged, StockIssueStates.Disputed, StockIssueStates.Cancelled],
        StockIssueStates.Issued,
        [StockIssueStates.Acknowledged, StockIssueStates.Cancelled],
        [
            new("SI1", [StateMachine.Start], DocumentCommands.IssueStock, StockIssueStates.Issued, ["WAREHOUSE"]),
            new("SI2", [StockIssueStates.Issued], DocumentCommands.AcknowledgeStockIssue, StockIssueStates.Acknowledged, ["TECHNICIAN"]),
            new("SI3", [StockIssueStates.Issued], DocumentCommands.DisputeStockIssue, StockIssueStates.Disputed, ["TECHNICIAN"]),
            new("SI4", [StockIssueStates.Issued, StockIssueStates.Disputed], DocumentCommands.CancelStockIssue, StockIssueStates.Cancelled, ["WAREHOUSE"]),
        ]);

    /// <summary>docs/lean-pilot/04 §4.10 (stock-return.yaml), plus the warehouse-recorded return of an offboarded technician.</summary>
    public static readonly StateMachine StockReturn = new(
        "StockReturn",
        [StockReturnStates.Declared, StockReturnStates.Received, StockReturnStates.Rejected],
        StockReturnStates.Declared,
        [StockReturnStates.Received, StockReturnStates.Rejected],
        [
            new("SR1", [StateMachine.Start], DocumentCommands.DeclareStockReturn, StockReturnStates.Declared, ["TECHNICIAN"]),
            new("SR2", [StockReturnStates.Declared], DocumentCommands.ReceiveStockReturn, StockReturnStates.Received, ["WAREHOUSE"]),
            new("SR3", [StockReturnStates.Declared], DocumentCommands.RejectStockReturn, StockReturnStates.Rejected, ["WAREHOUSE"]),
            new("SR4", [StateMachine.Start], DocumentCommands.ReceiveOffboardingReturn, StockReturnStates.Received, ["WAREHOUSE"]),
        ]);

    /// <summary>docs/lean-pilot/04 §4.11 (core-return.yaml).</summary>
    public static readonly StateMachine CoreReturn = new(
        "CoreReturn",
        [CoreReturnStates.Open, CoreReturnStates.DeclaredWithPart, CoreReturnStates.DeclaredUnrecoverable, CoreReturnStates.Received, CoreReturnStates.Waived],
        CoreReturnStates.Open,
        [CoreReturnStates.Received, CoreReturnStates.Waived],
        [
            new("CR1", [StateMachine.Start], DocumentCommands.ConsumeMaterial, CoreReturnStates.Open, ["assignee"]),
            new("CR2", [CoreReturnStates.Open], DocumentCommands.DeclareCoreReturnWithPart, CoreReturnStates.DeclaredWithPart, ["assignee"]),
            new("CR3", [CoreReturnStates.Open], DocumentCommands.DeclareCoreReturnUnrecoverable, CoreReturnStates.DeclaredUnrecoverable, ["assignee"]),
            new("CR4", [CoreReturnStates.DeclaredWithPart], DocumentCommands.ReceiveCorePart, CoreReturnStates.Received, ["WAREHOUSE"]),
            new("CR5", [CoreReturnStates.DeclaredWithPart], DocumentCommands.RecordCorePartNotPresented, CoreReturnStates.DeclaredUnrecoverable, ["WAREHOUSE"]),
            new("CR6", [CoreReturnStates.DeclaredUnrecoverable], DocumentCommands.WaiveCoreReturn, CoreReturnStates.Waived, ["SUPERVISOR"]),
        ]);
}

/// <summary>
/// A stock issue to one technician (04 §4.9). Its lines are its ISSUE movements (WAREHOUSE → IN_TRANSIT). The material is in
/// the technician's custody only after they acknowledge it (two-party handover).
/// </summary>
public sealed class StockIssue : IAuditable
{
    private StockIssue() { }

    public StockIssue(Guid id, string code, Guid technicianId, Guid issuedBy, DateTimeOffset issuedAt, string? note, Guid? referenceWorkOrderId, string? requestedBy)
    {
        Id = id;
        Code = code;
        TechnicianId = technicianId;
        IssuedBy = issuedBy;
        IssuedAt = issuedAt;
        Note = note;
        ReferenceWorkOrderId = referenceWorkOrderId;
        RequestedBy = requestedBy;
        State = StockIssueStates.Issued;
    }

    public string AuditEntityType => "stock_issue";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid TechnicianId { get; private set; }
    public string State { get; private set; } = StockIssueStates.Issued;
    public Guid IssuedBy { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Optional reference only: the work order the material is meant for. It is not a consumption.</summary>
    public Guid? ReferenceWorkOrderId { get; private set; }

    /// <summary>Free text (the request/approval workflow is deferred, 07 §7.4).</summary>
    public string? RequestedBy { get; private set; }

    /// <summary>When the technician acknowledged or disputed (trusted time of the phone command).</summary>
    public DateTimeOffset? RespondedAt { get; private set; }
    public string? DisputeReason { get; private set; }
    public Guid? DisputeEvidenceId { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>Set when the ISSUE_UNACKNOWLEDGED flag was raised, so the timer raises it once.</summary>
    public DateTimeOffset? UnacknowledgedFlaggedAt { get; private set; }
    public int Version { get; private set; }

    public Result<Transition> Acknowledge(DateTimeOffset at) => Apply(DocumentCommands.AcknowledgeStockIssue, () => RespondedAt = at);

    public Result<Transition> Dispute(string reason, Guid? evidenceId, DateTimeOffset at) => Apply(DocumentCommands.DisputeStockIssue, () =>
    {
        RespondedAt = at;
        DisputeReason = reason;
        DisputeEvidenceId = evidenceId;
    });

    public Result<Transition> Cancel(Guid by, string reason, DateTimeOffset at) => Apply(DocumentCommands.CancelStockIssue, () =>
    {
        CancelledBy = by;
        CancelledAt = at;
        CancelReason = reason;
    });

    public void MarkUnacknowledgedFlagged(DateTimeOffset at) => UnacknowledgedFlaggedAt = at;

    private Result<Transition> Apply(string command, Action effect)
    {
        var t = StockDocumentMachines.StockIssue.Resolve(State, command);
        if (!t.IsSuccess) return t;
        effect();
        State = t.Value.To;
        return t;
    }
}

/// <summary>
/// A return of unused material (04 §4.10): declared by the technician (CUSTODY → IN_TRANSIT), then received (→ WAREHOUSE,
/// serials matched) or rejected (REVERSAL back to CUSTODY) by the warehouse. Its lines are its RETURN_DECLARE movements.
/// An OFFBOARDING return is recorded by the warehouse directly (CUSTODY → WAREHOUSE) because a disabled user cannot act.
/// </summary>
public sealed class StockReturn : IAuditable
{
    private StockReturn() { }

    private StockReturn(Guid id, string code, Guid technicianId, string kind, string state, Guid declaredBy, DateTimeOffset declaredAt, string? note)
    {
        Id = id;
        Code = code;
        TechnicianId = technicianId;
        Kind = kind;
        State = state;
        DeclaredBy = declaredBy;
        DeclaredAt = declaredAt;
        Note = note;
    }

    public static StockReturn Declare(Guid id, string code, Guid technicianId, DateTimeOffset at, string? note) =>
        new(id, code, technicianId, StockReturnKinds.Unused, StockReturnStates.Declared, technicianId, at, note);

    /// <summary>SR4: recorded and received in one step by the warehouse officer (declared_by = the officer).</summary>
    public static StockReturn ReceivedOnOffboarding(Guid id, string code, Guid technicianId, Guid warehouseOfficer, DateTimeOffset at, string note)
    {
        var r = new StockReturn(id, code, technicianId, StockReturnKinds.Offboarding, StockReturnStates.Received, warehouseOfficer, at, note)
        {
            DecidedBy = warehouseOfficer,
            DecidedAt = at,
        };
        return r;
    }

    public string AuditEntityType => "stock_return";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid TechnicianId { get; private set; }
    public string Kind { get; private set; } = StockReturnKinds.Unused;
    public string State { get; private set; } = StockReturnStates.Declared;
    public Guid DeclaredBy { get; private set; }
    public DateTimeOffset DeclaredAt { get; private set; }
    public string? Note { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? RejectReason { get; private set; }
    public int Version { get; private set; }

    public Result<Transition> Receive(Guid by, DateTimeOffset at) => Apply(DocumentCommands.ReceiveStockReturn, by, at, null);

    public Result<Transition> Reject(Guid by, DateTimeOffset at, string reason) => Apply(DocumentCommands.RejectStockReturn, by, at, reason);

    private Result<Transition> Apply(string command, Guid by, DateTimeOffset at, string? reason)
    {
        var t = StockDocumentMachines.StockReturn.Resolve(State, command);
        if (!t.IsSuccess) return t;
        DecidedBy = by;
        DecidedAt = at;
        RejectReason = reason;
        State = t.Value.To;
        return t;
    }
}

/// <summary>
/// The obligation to bring the failed part back (04 §4.11), opened by the consumption of a core_return_required item on a
/// work order. <see cref="ExpectedSerial"/> is the serial known to be installed on the light before this repair (if any);
/// the part presented at the warehouse is checked against it.
/// </summary>
public sealed class CoreReturn : IAuditable
{
    private CoreReturn() { }

    public CoreReturn(Guid id, string code, Guid workOrderId, Guid assetId, Guid itemId, Guid technicianId, Guid consumeMovementId, decimal quantity,
        string? installedSerial, string? expectedSerial, DateTimeOffset createdAt, string workOrderCode, string? assetCode)
    {
        Id = id;
        WorkOrderCode = workOrderCode;
        AssetCode = assetCode;
        Code = code;
        WorkOrderId = workOrderId;
        AssetId = assetId;
        ItemId = itemId;
        TechnicianId = technicianId;
        ConsumeMovementId = consumeMovementId;
        Quantity = quantity;
        InstalledSerial = installedSerial;
        ExpectedSerial = expectedSerial;
        CreatedAt = createdAt;
        State = CoreReturnStates.Open;
    }

    public string AuditEntityType => "core_return";

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public Guid WorkOrderId { get; private set; }

    /// <summary>Copied from the work order when the obligation opens, so warehouse lists read without another module.</summary>
    public string WorkOrderCode { get; private set; } = string.Empty;
    public Guid AssetId { get; private set; }
    public string? AssetCode { get; private set; }
    public Guid ItemId { get; private set; }

    /// <summary>The technician who consumed the new part (and removed the failed one).</summary>
    public Guid TechnicianId { get; private set; }
    public Guid ConsumeMovementId { get; private set; }
    public decimal Quantity { get; private set; }

    /// <summary>Serial of the new part installed by the consumption (serial-tracked items).</summary>
    public string? InstalledSerial { get; private set; }

    /// <summary>Serial known to be installed on the light before this repair; null when unknown (first repair after registration).</summary>
    public string? ExpectedSerial { get; private set; }
    public string State { get; private set; } = CoreReturnStates.Open;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? DeclaredBy { get; private set; }
    public DateTimeOffset? DeclaredAt { get; private set; }
    public string? DeclaredSerial { get; private set; }
    public string? DeclaredReason { get; private set; }
    public Guid? EvidenceId { get; private set; }
    public Guid? ReceivedBy { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public string? ReceivedSerial { get; private set; }

    /// <summary>The presented serial differs from the reference serial (CRITICAL flag CORE_SERIAL_MISMATCH; the receipt is still recorded).</summary>
    public bool SerialMismatch { get; private set; }
    public Guid? ReceiveMovementId { get; private set; }
    public string? NotPresentedReason { get; private set; }
    public Guid? WaivedBy { get; private set; }
    public DateTimeOffset? WaivedAt { get; private set; }
    public string? WaiveReason { get; private set; }

    /// <summary>Set when CORE_RETURN_OVERDUE was raised, so the timer raises it once.</summary>
    public DateTimeOffset? OverdueFlaggedAt { get; private set; }
    public int Version { get; private set; }

    /// <summary>The serial the presented part is checked against: the known installed serial, else the one the technician declared.</summary>
    public string? ReferenceSerial => ExpectedSerial ?? DeclaredSerial;

    public Result<Transition> DeclareWithPart(Guid by, DateTimeOffset at, string? serial, Guid evidenceId) =>
        Apply(DocumentCommands.DeclareCoreReturnWithPart, () =>
        {
            DeclaredBy = by;
            DeclaredAt = at;
            DeclaredSerial = serial;
            EvidenceId = evidenceId;
        });

    public Result<Transition> DeclareUnrecoverable(Guid by, DateTimeOffset at, string reason, Guid evidenceId) =>
        Apply(DocumentCommands.DeclareCoreReturnUnrecoverable, () =>
        {
            DeclaredBy = by;
            DeclaredAt = at;
            DeclaredReason = reason;
            EvidenceId = evidenceId;
        });

    public Result<Transition> Receive(Guid by, DateTimeOffset at, string? serial, bool mismatch, Guid movementId) =>
        Apply(DocumentCommands.ReceiveCorePart, () =>
        {
            ReceivedBy = by;
            ReceivedAt = at;
            ReceivedSerial = serial;
            SerialMismatch = mismatch;
            ReceiveMovementId = movementId;
        });

    public Result<Transition> NotPresented(Guid by, DateTimeOffset at, string reason) =>
        Apply(DocumentCommands.RecordCorePartNotPresented, () =>
        {
            ReceivedBy = by;
            ReceivedAt = at;
            NotPresentedReason = reason;
        });

    public Result<Transition> Waive(Guid by, DateTimeOffset at, string reason) =>
        Apply(DocumentCommands.WaiveCoreReturn, () =>
        {
            WaivedBy = by;
            WaivedAt = at;
            WaiveReason = reason;
        });

    public void MarkOverdueFlagged(DateTimeOffset at) => OverdueFlaggedAt = at;

    private Result<Transition> Apply(string command, Action effect)
    {
        var t = StockDocumentMachines.CoreReturn.Resolve(State, command);
        if (!t.IsSuccess) return t;
        effect();
        State = t.Value.To;
        return t;
    }
}

/// <summary>
/// One serial-tracked unit and where it is installed (07 §7.2: consuming a serial-tracked item on a light sets
/// installed_asset_id). Its movements through stock stay in the ledger; this row answers "which unit is on this light now".
/// </summary>
public sealed class SerialUnit : IAuditable
{
    private SerialUnit() { }

    public SerialUnit(Guid id, Guid itemId, string serial, bool historical)
    {
        Id = id;
        ItemId = itemId;
        Serial = serial;
        Historical = historical;
    }

    public string AuditEntityType => "serial_unit";

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public string Serial { get; private set; } = string.Empty;
    public Guid? InstalledAssetId { get; private set; }
    public DateTimeOffset? InstalledAt { get; private set; }
    public Guid? InstalledByMovementId { get; private set; }
    public Guid? RemovedFromAssetId { get; private set; }
    public DateTimeOffset? RemovedAt { get; private set; }

    /// <summary>
    /// Known only from a core-return declaration (the part was on the light before the platform knew it): recorded as the
    /// historical installed serial of that light (07 §7.2).
    /// </summary>
    public bool Historical { get; private set; }
    public int Version { get; private set; }

    public void Install(Guid assetId, Guid movementId, DateTimeOffset at)
    {
        InstalledAssetId = assetId;
        InstalledAt = at;
        InstalledByMovementId = movementId;
        RemovedFromAssetId = null;
        RemovedAt = null;
    }

    public void Remove(Guid assetId, DateTimeOffset at)
    {
        InstalledAssetId = null;
        RemovedFromAssetId = assetId;
        RemovedAt = at;
    }
}
