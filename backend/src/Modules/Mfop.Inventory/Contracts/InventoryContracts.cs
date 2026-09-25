namespace Mfop.Inventory.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency, DateOnly AsOf);

public sealed record ItemDto(Guid Id, string Sku, string NameAr, string NameEn, string Unit, bool SerialTracked, bool CoreReturnRequired, MoneyDto? Cost);

public sealed record StockLine(Guid ItemId, decimal Quantity, IReadOnlyList<string>? Serials);

public sealed record ReceiptRequest(IReadOnlyList<StockLine> Lines, string? Note);

/// <param name="ReferenceWorkOrderId">Optional reference only (07 §7.2); it is not a consumption on that work order.</param>
/// <param name="RequestedBy">Free text: who asked for the material (the request/approval workflow is deferred).</param>
public sealed record IssueRequest(Guid TechnicianId, IReadOnlyList<StockLine> Lines, string? Note, Guid? ReferenceWorkOrderId = null, string? RequestedBy = null);

public sealed record IssueCreated(Guid IssueId, string Code, IReadOnlyList<Guid> MovementIds);

public sealed record ReverseRequest(string? Reason);

/// <param name="ConfirmedSerials">The serials the warehouse officer counted (must equal the declared serials).</param>
public sealed record ReceiveReturnRequest(IReadOnlyList<string>? ConfirmedSerials, string? Note);

/// <param name="Outcome">RECEIVED (the part is handed over) or NOT_PRESENTED (the technician did not bring it).</param>
/// <param name="Serial">Serial read from the presented part (serial-tracked items).</param>
public sealed record ReceiveCoreRequest(string? Outcome, string? Serial, string? Reason);

/// <param name="Final">True when nothing more will be returned: what is still in the custody is then flagged CUSTODY_NOT_RETURNED.</param>
public sealed record OffboardingReturnRequest(Guid TechnicianId, IReadOnlyList<StockLine> Lines, bool Final, string? Note);

public sealed record BalanceDto(Guid LocationId, string LocationKind, string LocationName, Guid? CustodianUserId, string? CustodianName,
    Guid ItemId, string Sku, string ItemNameAr, string ItemNameEn, string Unit, decimal Quantity);

public sealed record CustodyItem(Guid ItemId, string Sku, string NameAr, string NameEn, string Unit, bool SerialTracked, decimal Quantity, IReadOnlyList<string> Serials);

public sealed record MovementDto(Guid Id, string Type, Guid ItemId, string Sku, string ItemNameAr, string ItemNameEn, string Unit, decimal Quantity,
    IReadOnlyList<string> Serials, Guid? WorkOrderId, Guid? AssetId, Guid ActorId, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt,
    bool ExceedsCustody, bool SerialNotInCustody, MoneyDto? UnitCost, MoneyDto? TotalCost, Guid FromLocationId, Guid ToLocationId);

/// <summary>One line of a stock document: one ledger movement.</summary>
public sealed record DocumentLineDto(Guid MovementId, Guid ItemId, string Sku, string NameAr, string NameEn, string Unit, bool SerialTracked, decimal Quantity,
    IReadOnlyList<string> Serials);

public sealed record DocumentFlagDto(Guid Id, string RuleCode, string Severity, string State, DateTimeOffset CreatedAt);

public sealed record StockIssueDto(
    Guid Id, string Code, string State, Guid TechnicianId, string TechnicianName, Guid IssuedBy, string IssuedByName, DateTimeOffset IssuedAt, string? Note,
    Guid? ReferenceWorkOrderId, string? RequestedBy, DateTimeOffset? RespondedAt, string? DisputeReason, Guid? DisputeEvidenceId,
    DateTimeOffset? CancelledAt, string? CancelledByName, string? CancelReason, bool Overdue, IReadOnlyList<DocumentLineDto> Lines,
    IReadOnlyList<DocumentFlagDto> Flags);

public sealed record StockReturnDto(
    Guid Id, string Code, string Kind, string State, Guid TechnicianId, string TechnicianName, Guid DeclaredBy, string DeclaredByName, DateTimeOffset DeclaredAt,
    string? Note, DateTimeOffset? DecidedAt, string? DecidedByName, string? RejectReason, IReadOnlyList<DocumentLineDto> Lines, IReadOnlyList<DocumentFlagDto> Flags);

public sealed record CoreReturnDto(
    Guid Id, string Code, string State, Guid ConsumeMovementId, Guid WorkOrderId, string WorkOrderCode, Guid AssetId, string? AssetCode, Guid ItemId, string Sku, string NameAr,
    string NameEn, bool SerialTracked, decimal Quantity, Guid TechnicianId, string TechnicianName, string? InstalledSerial, string? ExpectedSerial,
    DateTimeOffset CreatedAt, DateTimeOffset? DeclaredAt, string? DeclaredSerial, string? DeclaredReason, Guid? EvidenceId, DateTimeOffset? ReceivedAt,
    string? ReceivedByName, string? ReceivedSerial, bool SerialMismatch, string? NotPresentedReason, DateTimeOffset? WaivedAt, string? WaivedByName,
    string? WaiveReason, bool Overdue, IReadOnlyList<DocumentFlagDto> Flags);

/// <summary>What the phone needs about the technician's stock documents (in the pull).</summary>
/// <param name="Issues">Issues waiting for the technician (ISSUED) or disputed by them (DISPUTED).</param>
/// <param name="Returns">Unused returns declared and not yet decided by the warehouse.</param>
/// <param name="CoreReturns">Core-return obligations that are not finished (OPEN, DECLARED_*).</param>
public sealed record TechnicianStockDto(IReadOnlyList<StockIssueDto> Issues, IReadOnlyList<StockReturnDto> Returns, IReadOnlyList<CoreReturnDto> CoreReturns);

/// <summary>A ledger movement with its locations, actor and document, for stock history.</summary>
public sealed record StockHistoryEntry(
    Guid Id, string Type, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt, Guid ItemId, string Sku, string Unit, decimal Quantity, IReadOnlyList<string> Serials,
    Guid FromLocationId, string FromKind, string FromName, Guid ToLocationId, string ToKind, string ToName, Guid ActorId, string ActorName,
    Guid? DocumentId, string? DocumentCode, Guid? WorkOrderId, Guid? AssetId, Guid? ReversalOf, string? Note, bool ExceedsCustody, bool SerialNotInCustody);

/// <param name="Location">Where the serial is now (the destination of its latest movement), or null if it never moved.</param>
public sealed record SerialStatusDto(Guid ItemId, string Sku, string Serial, string? LocationKind, string? LocationName, Guid? CustodianUserId,
    Guid? InstalledAssetId, DateTimeOffset? InstalledAt, Guid? RemovedFromAssetId, DateTimeOffset? RemovedAt, bool Historical);

public sealed record StockHistoryDto(IReadOnlyList<StockHistoryEntry> Movements, SerialStatusDto? Serial, bool Truncated);

/// <summary>Quantities of one item in every kind of location (the ledger is balanced: every movement leaves one location and enters another).</summary>
public sealed record ReconciliationItemDto(
    Guid ItemId, string Sku, string NameAr, string NameEn, string Unit, decimal ReceivedNet, decimal Warehouse, decimal InTransit, decimal Custody,
    decimal Consumed, decimal CoreBin, decimal CoresExpected, decimal InTransitOpenDocuments);

public sealed record CustodyHolderDto(Guid UserId, string Name, bool Active, IReadOnlyList<CustodyItem> Items, int OpenIssues, int OpenReturns, int OpenCoreReturns);

/// <param name="Code">BALANCE_MISMATCH, IN_TRANSIT_MISMATCH, SERIAL_COUNT_MISMATCH, NEGATIVE_BALANCE, CORE_BIN_MISMATCH, CUSTODY_OF_DISABLED_USER.</param>
public sealed record ReconciliationFinding(string Code, string Severity, string Message, Guid? ItemId, Guid? LocationId, Guid? UserId);

public sealed record OpenDocumentsDto(int IssuesAwaitingAcknowledgment, int IssuesOverdue, int IssuesDisputed, int ReturnsDeclared, int CoreReturnsOpen,
    int CoreReturnsDeclaredWithPart, int CoreReturnsOverdue, int CoreReturnsUnrecoverable);

/// <param name="Balanced">True when there is no finding of severity WARNING or CRITICAL.</param>
public sealed record ReconciliationDto(DateTimeOffset GeneratedAt, bool Balanced, int MovementsChecked, IReadOnlyList<ReconciliationItemDto> Items,
    IReadOnlyList<CustodyHolderDto> Custodians, OpenDocumentsDto Open, IReadOnlyList<ReconciliationFinding> Findings);

/// <param name="MovementId">Generated on the phone (idempotent with the command id).</param>
/// <param name="WorkOrderCode">Kept on a core-return obligation so the warehouse sees which work order it came from.</param>
public sealed record ConsumeRequest(Guid MovementId, Guid ItemId, decimal Quantity, IReadOnlyList<string> Serials, Guid TechnicianId,
    Guid WorkOrderId, Guid AssetId, DateTimeOffset OccurredAt, Guid CommandId, string WorkOrderCode = "", string? AssetCode = null);

/// <param name="CustodyBefore">Known custody balance of the item before this consumption.</param>
/// <param name="CoreReturnIds">Core-return obligations opened by this consumption (core_return_required items).</param>
public sealed record ConsumeOutcome(Guid MovementId, bool ExceedsCustody, bool SerialNotInCustody, decimal CustodyBefore, IReadOnlyList<Guid>? CoreReturnIds = null);

/// <summary>A photo taken in the app, announced with its device hash (the bytes follow with PUT /sync/v1/evidence/{id}/content).</summary>
public sealed record PhotoAnnouncement(Guid EvidenceId, string? Sha256, long SizeBytes, string? Mime, DateTimeOffset? CapturedAt);

/// <param name="Outcome">WITH_PART (photo, serial when serial-tracked) or UNRECOVERABLE (reason, photo).</param>
public sealed record DeclareCoreReturnRequest(Guid CoreReturnId, string? Outcome, string? Serial, string? Reason, PhotoAnnouncement? Photo,
    Guid ActorId, Guid? DeviceId, DateTimeOffset At);

/// <summary>Material ledger and core-return obligations used by the WorkOrders module inside its unit of work.</summary>
public interface IInventoryLedger
{
    Task<Mfop.Kernel.Results.Result<ConsumeOutcome>> ConsumeAsync(ConsumeRequest request, CancellationToken ct);

    Task<IReadOnlyList<MovementDto>> MovementsForWorkOrdersAsync(IReadOnlyCollection<Guid> workOrderIds, CancellationToken ct);

    Task<IReadOnlyList<MovementDto>> MovementsForAssetAsync(Guid assetId, CancellationToken ct);

    Task<IReadOnlyList<CustodyItem>> CustodyOfAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<ItemDto>> ItemsAsync(CancellationToken ct);

    /// <summary>M5: the core-return obligations of work orders (W9 needs every one declared before submission).</summary>
    Task<IReadOnlyList<CoreReturnDto>> CoreReturnsForWorkOrdersAsync(IReadOnlyCollection<Guid> workOrderIds, CancellationToken ct);

    /// <summary>M5: DeclareCoreReturn inside the caller's unit of work (the caller checked the work order and the actor).</summary>
    Task<Mfop.Kernel.Results.Result<Guid>> DeclareCoreReturnAsync(DeclareCoreReturnRequest request, CancellationToken ct);

    /// <summary>M5: pending issues, open returns and unfinished core returns of a technician (for the phone).</summary>
    Task<TechnicianStockDto> TechnicianStockAsync(Guid userId, CancellationToken ct);
}
