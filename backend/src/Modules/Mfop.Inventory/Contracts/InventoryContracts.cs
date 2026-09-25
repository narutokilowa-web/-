namespace Mfop.Inventory.Contracts;

public sealed record MoneyDto(decimal Amount, string Currency, DateOnly AsOf);

public sealed record ItemDto(Guid Id, string Sku, string NameAr, string NameEn, string Unit, bool SerialTracked, bool CoreReturnRequired, MoneyDto? Cost);

public sealed record StockLine(Guid ItemId, decimal Quantity, IReadOnlyList<string>? Serials);

public sealed record ReceiptRequest(IReadOnlyList<StockLine> Lines, string? Note);

public sealed record IssueRequest(Guid TechnicianId, IReadOnlyList<StockLine> Lines, string? Note);

public sealed record ReverseRequest(string? Reason);

public sealed record BalanceDto(Guid LocationId, string LocationKind, string LocationName, Guid? CustodianUserId, string? CustodianName,
    Guid ItemId, string Sku, string ItemNameAr, string ItemNameEn, string Unit, decimal Quantity);

public sealed record CustodyItem(Guid ItemId, string Sku, string NameAr, string NameEn, string Unit, bool SerialTracked, decimal Quantity, IReadOnlyList<string> Serials);

public sealed record MovementDto(Guid Id, string Type, Guid ItemId, string Sku, string ItemNameAr, string ItemNameEn, string Unit, decimal Quantity,
    IReadOnlyList<string> Serials, Guid? WorkOrderId, Guid? AssetId, Guid ActorId, DateTimeOffset OccurredAt, DateTimeOffset RecordedAt,
    bool ExceedsCustody, bool SerialNotInCustody, MoneyDto? UnitCost, MoneyDto? TotalCost, Guid FromLocationId, Guid ToLocationId);

/// <param name="MovementId">Generated on the phone (idempotent with the command id).</param>
public sealed record ConsumeRequest(Guid MovementId, Guid ItemId, decimal Quantity, IReadOnlyList<string> Serials, Guid TechnicianId,
    Guid WorkOrderId, Guid AssetId, DateTimeOffset OccurredAt, Guid CommandId);

/// <param name="CustodyBefore">Known custody balance of the item before this consumption.</param>
public sealed record ConsumeOutcome(Guid MovementId, bool ExceedsCustody, bool SerialNotInCustody, decimal CustodyBefore);

/// <summary>Consumption on a work order (CUSTODY → CONSUMED), called by the WorkOrders module inside its unit of work.</summary>
public interface IInventoryLedger
{
    Task<Mfop.Kernel.Results.Result<ConsumeOutcome>> ConsumeAsync(ConsumeRequest request, CancellationToken ct);

    Task<IReadOnlyList<MovementDto>> MovementsForWorkOrdersAsync(IReadOnlyCollection<Guid> workOrderIds, CancellationToken ct);

    Task<IReadOnlyList<MovementDto>> MovementsForAssetAsync(Guid assetId, CancellationToken ct);

    Task<IReadOnlyList<CustodyItem>> CustodyOfAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<ItemDto>> ItemsAsync(CancellationToken ct);
}
