using Mfop.Kernel.Auditing;
using Mfop.Kernel.StateMachines;

namespace Mfop.Inventory.Domain;

public static class LocationKinds
{
    public const string Supplier = "SUPPLIER", Warehouse = "WAREHOUSE", Custody = "CUSTODY", Consumed = "CONSUMED";

    /// <summary>M5: material between two parties (issued, not yet acknowledged; declared for return, not yet received).</summary>
    public const string InTransit = "IN_TRANSIT";

    /// <summary>M5: failed parts received back from the field (core returns).</summary>
    public const string CoreBin = "CORE_BIN";

    /// <summary>
    /// M5: virtual source of parts removed from lights. A failed part was never in stock, so its receipt into CORE_BIN is
    /// balanced against this location (its balance is minus the cores received, like SUPPLIER for receipts).
    /// </summary>
    public const string Field = "FIELD";
}

public static class MovementTypes
{
    public const string Receipt = "RECEIPT", Issue = "ISSUE", Consume = "CONSUME", Reversal = "REVERSAL";

    // M5 (docs/lean-pilot/04 §4.9–4.11)
    public const string Acknowledge = "ACKNOWLEDGE", ReturnDeclare = "RETURN_DECLARE", ReturnReceive = "RETURN_RECEIVE", CoreReceive = "CORE_RECEIVE";
}

public static class LedgerCommands
{
    public const string DefineSupplierLocation = nameof(DefineSupplierLocation);
    public const string RecordReceipt = nameof(RecordReceipt);
    public const string IssueStock = nameof(IssueStock);
    public const string ConsumeMaterial = nameof(ConsumeMaterial);
    public const string ReverseReceipt = nameof(ReverseReceipt);
    public const string DefineFieldLocation = nameof(DefineFieldLocation);

    // M5
    public const string CancelStockIssue = nameof(CancelStockIssue);
    public const string AcknowledgeStockIssue = nameof(AcknowledgeStockIssue);
    public const string DeclareStockReturn = nameof(DeclareStockReturn);
    public const string ReceiveStockReturn = nameof(ReceiveStockReturn);
    public const string RejectStockReturn = nameof(RejectStockReturn);
    public const string ReceiveCorePart = nameof(ReceiveCorePart);
    public const string ReceiveOffboardingReturn = nameof(ReceiveOffboardingReturn);
}

/// <summary>
/// The allowed movements of the append-only ledger, as a table over location kinds.
/// MUST equal docs/state-machines/stock-ledger.yaml (StateMachineConformanceTests).
/// M5: issue goes through IN_TRANSIT until the technician acknowledges; unused returns go back through IN_TRANSIT; failed
/// parts are received into CORE_BIN. Corrections are REVERSAL movements of the command that owns the flow (no free adjustment).
/// </summary>
public static class InventoryStateMachines
{
    public static readonly StateMachine StockLedger = new(
        "StockLedger",
        [LocationKinds.Supplier, LocationKinds.Warehouse, LocationKinds.InTransit, LocationKinds.Custody, LocationKinds.Consumed, LocationKinds.Field,
         LocationKinds.CoreBin],
        LocationKinds.Supplier,
        [LocationKinds.Consumed, LocationKinds.CoreBin],
        [
            new("S0", [StateMachine.Start], LedgerCommands.DefineSupplierLocation, LocationKinds.Supplier, ["system"]),
            new("S1", [LocationKinds.Supplier], LedgerCommands.RecordReceipt, LocationKinds.Warehouse, ["WAREHOUSE"]),
            new("S2", [LocationKinds.Warehouse], LedgerCommands.IssueStock, LocationKinds.InTransit, ["WAREHOUSE"]),
            new("S3", [LocationKinds.Custody], LedgerCommands.ConsumeMaterial, LocationKinds.Consumed, ["assignee"]),
            new("S4", [LocationKinds.Warehouse], LedgerCommands.ReverseReceipt, LocationKinds.Supplier, ["WAREHOUSE"]),
            new("S5", [LocationKinds.InTransit], LedgerCommands.CancelStockIssue, LocationKinds.Warehouse, ["WAREHOUSE"]),
            new("S6", [LocationKinds.InTransit], LedgerCommands.AcknowledgeStockIssue, LocationKinds.Custody, ["TECHNICIAN"]),
            new("S7", [LocationKinds.Custody], LedgerCommands.DeclareStockReturn, LocationKinds.InTransit, ["TECHNICIAN"]),
            new("S8", [LocationKinds.InTransit], LedgerCommands.ReceiveStockReturn, LocationKinds.Warehouse, ["WAREHOUSE"]),
            new("S9", [LocationKinds.InTransit], LedgerCommands.RejectStockReturn, LocationKinds.Custody, ["WAREHOUSE"]),
            new("S10", [StateMachine.Start], LedgerCommands.DefineFieldLocation, LocationKinds.Field, ["system"]),
            new("S11", [LocationKinds.Field], LedgerCommands.ReceiveCorePart, LocationKinds.CoreBin, ["WAREHOUSE"]),
            new("S12", [LocationKinds.Custody], LedgerCommands.ReceiveOffboardingReturn, LocationKinds.Warehouse, ["WAREHOUSE"]),
        ]);
}

public sealed class Item : IAuditable
{
    private Item() { }

    public Item(Guid id, string sku, string nameAr, string nameEn, string unit, bool serialTracked, bool coreReturnRequired)
    {
        Id = id;
        Sku = sku;
        NameAr = nameAr;
        NameEn = nameEn;
        Unit = unit;
        SerialTracked = serialTracked;
        CoreReturnRequired = coreReturnRequired;
    }

    public string AuditEntityType => "item";

    public Guid Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>EA (each) or M (metre).</summary>
    public string Unit { get; private set; } = "EA";
    public bool SerialTracked { get; private set; }

    /// <summary>M5: consuming it opens a core-return obligation (the failed part comes back to the warehouse).</summary>
    public bool CoreReturnRequired { get; private set; }
    public int Version { get; private set; }
}

/// <summary>Standard cost history, append-only. Money = amount + ISO currency + as-of date (no conversion anywhere).</summary>
public sealed class ItemCost : IAuditable
{
    private ItemCost() { }

    public ItemCost(Guid id, Guid itemId, decimal amount, string currency, DateOnly asOf, Guid? setBy, DateTimeOffset setAt)
    {
        Id = id;
        ItemId = itemId;
        Amount = amount;
        Currency = currency;
        AsOf = asOf;
        SetBy = setBy;
        SetAt = setAt;
    }

    public string AuditEntityType => "item_cost";

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateOnly AsOf { get; private set; }
    public Guid? SetBy { get; private set; }
    public DateTimeOffset SetAt { get; private set; }
}

public sealed class StockLocation : IAuditable
{
    private StockLocation() { }

    public StockLocation(Guid id, string kind, string name, Guid? custodianUserId)
    {
        Id = id;
        Kind = kind;
        Name = name;
        CustodianUserId = custodianUserId;
    }

    public string AuditEntityType => "stock_location";

    public Guid Id { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid? CustodianUserId { get; private set; }
}

/// <summary>One ledger movement. Append-only: a correction is a REVERSAL movement, never an edit.</summary>
public sealed class StockMovement : IAuditable
{
    private StockMovement() { }

    public StockMovement(Guid id, string type, Guid itemId, decimal quantity, Guid fromLocationId, Guid toLocationId, string[] serials,
        Guid? workOrderId, Guid? assetId, Guid? reversalOf, Guid actorId, Guid commandId, DateTimeOffset occurredAt, DateTimeOffset recordedAt,
        string? note, bool exceedsCustody = false, bool serialNotInCustody = false, Guid? documentId = null)
    {
        Id = id;
        Type = type;
        ItemId = itemId;
        Quantity = quantity;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        Serials = serials;
        WorkOrderId = workOrderId;
        AssetId = assetId;
        ReversalOf = reversalOf;
        ActorId = actorId;
        CommandId = commandId;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        Note = note;
        ExceedsCustody = exceedsCustody;
        SerialNotInCustody = serialNotInCustody;
        DocumentId = documentId;
    }

    public string AuditEntityType => "stock_movement";

    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public Guid ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid FromLocationId { get; private set; }
    public Guid ToLocationId { get; private set; }
    public string[] Serials { get; private set; } = [];
    public Guid? WorkOrderId { get; private set; }
    public Guid? AssetId { get; private set; }
    public Guid? ReversalOf { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid CommandId { get; private set; }

    /// <summary>When it happened (the phone's capture time for consumption; server time otherwise).</summary>
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? Note { get; private set; }

    /// <summary>Consumption was applied although the technician's known custody was smaller ("field reality first, review after").</summary>
    public bool ExceedsCustody { get; private set; }
    public bool SerialNotInCustody { get; private set; }

    /// <summary>M5: the stock issue, stock return or core return this movement belongs to (null for receipts and consumption).</summary>
    public Guid? DocumentId { get; private set; }
}

/// <summary>Balance per (location, item), maintained in the same transaction as each movement; the verifier recomputes it.</summary>
public sealed class StockBalance : IAuditable
{
    private StockBalance() { }

    public StockBalance(Guid id, Guid locationId, Guid itemId)
    {
        Id = id;
        LocationId = locationId;
        ItemId = itemId;
    }

    public string AuditEntityType => "stock_balance";

    public Guid Id { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public int Version { get; private set; }

    public void Add(decimal delta) => Quantity += delta;
}

public static class Quantities
{
    /// <summary>numeric(12,3): the ledger keeps exactly three decimals.</summary>
    public static decimal Normalize(decimal q) => decimal.Round(q, 3, MidpointRounding.AwayFromZero);
}
