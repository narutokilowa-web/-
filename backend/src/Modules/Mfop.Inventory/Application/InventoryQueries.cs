using Mfop.Access.Contracts;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Domain;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Inventory.Application;

public sealed class InventoryQueries(InventoryDbContext db, IUserDirectory users, InventoryService service) : IInventoryLedger
{
    public async Task<IReadOnlyList<ItemDto>> ItemsAsync(CancellationToken ct)
    {
        var items = await db.Items.AsNoTracking().OrderBy(i => i.Sku).ToListAsync(ct);
        var costs = await db.ItemCosts.AsNoTracking().ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return items.Select(i => new ItemDto(i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, i.SerialTracked, i.CoreReturnRequired, CostAt(costs, i.Id, today))).ToList();
    }

    public async Task<IReadOnlyList<BalanceDto>> BalancesAsync(CancellationToken ct)
    {
        var balances = await db.Balances.AsNoTracking().ToListAsync(ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var names = await users.DisplayNamesAsync(locations.Values.Where(l => l.CustodianUserId is not null).Select(l => l.CustodianUserId!.Value), ct);
        return balances
            .Where(b => locations[b.LocationId].Kind is LocationKinds.Warehouse or LocationKinds.Custody or LocationKinds.Consumed)
            .Select(b =>
            {
                var l = locations[b.LocationId];
                var i = items[b.ItemId];
                return new BalanceDto(l.Id, l.Kind, l.Name, l.CustodianUserId, l.CustodianUserId is { } c ? names.GetValueOrDefault(c, "?") : null,
                    i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, b.Quantity);
            })
            .OrderBy(b => b.LocationKind == LocationKinds.Warehouse ? 0 : b.LocationKind == LocationKinds.Custody ? 1 : 2)
            .ThenBy(b => b.CustodianName, StringComparer.Ordinal)
            .ThenBy(b => b.Sku, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<CustodyItem>> CustodyOfAsync(Guid userId, CancellationToken ct)
    {
        var custody = await db.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Kind == LocationKinds.Custody && l.CustodianUserId == userId, ct);
        if (custody is null) return [];
        var balances = await db.Balances.AsNoTracking().Where(b => b.LocationId == custody.Id).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var serialMovements = await db.Movements.AsNoTracking().Where(m => m.Serials.Length > 0).OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var serialsAt = new Dictionary<(Guid Item, string Serial), Guid>();
        foreach (var m in serialMovements)
        {
            foreach (var s in m.Serials) serialsAt[(m.ItemId, s)] = m.ToLocationId;
        }

        return balances.Where(b => b.Quantity != 0).Select(b =>
        {
            var i = items[b.ItemId];
            var serials = serialsAt.Where(kv => kv.Key.Item == i.Id && kv.Value == custody.Id).Select(kv => kv.Key.Serial).Order(StringComparer.Ordinal).ToList();
            return new CustodyItem(i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, i.SerialTracked, b.Quantity, serials);
        }).OrderBy(c => c.Sku, StringComparer.Ordinal).ToList();
    }

    public Task<IReadOnlyList<MovementDto>> MovementsForWorkOrdersAsync(IReadOnlyCollection<Guid> workOrderIds, CancellationToken ct) =>
        MovementsAsync(db.Movements.AsNoTracking().Where(m => m.WorkOrderId != null && workOrderIds.Contains(m.WorkOrderId.Value)), ct);

    public Task<IReadOnlyList<MovementDto>> MovementsForAssetAsync(Guid assetId, CancellationToken ct) =>
        MovementsAsync(db.Movements.AsNoTracking().Where(m => m.AssetId == assetId), ct);

    public Task<Result<ConsumeOutcome>> ConsumeAsync(ConsumeRequest request, CancellationToken ct) =>
        service.ConsumeInCurrentUnitOfWorkAsync(request, ct);

    private async Task<IReadOnlyList<MovementDto>> MovementsAsync(IQueryable<StockMovement> query, CancellationToken ct)
    {
        var movements = await query.OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var costs = await db.ItemCosts.AsNoTracking().ToListAsync(ct);
        return movements.Select(m =>
        {
            var i = items[m.ItemId];
            var unit = CostAt(costs, i.Id, DateOnly.FromDateTime(m.OccurredAt.UtcDateTime));
            return new MovementDto(m.Id, m.Type, i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, m.Quantity, m.Serials, m.WorkOrderId, m.AssetId, m.ActorId,
                m.OccurredAt, m.RecordedAt, m.ExceedsCustody, m.SerialNotInCustody, unit,
                unit is null ? null : new MoneyDto(unit.Amount * m.Quantity, unit.Currency, unit.AsOf), m.FromLocationId, m.ToLocationId);
        }).ToList();
    }

    /// <summary>The standard cost effective on the date, in its own currency (no conversion; 07 §7.1).</summary>
    private static MoneyDto? CostAt(IReadOnlyList<ItemCost> costs, Guid itemId, DateOnly date) =>
        costs.Where(c => c.ItemId == itemId && c.AsOf <= date).OrderByDescending(c => c.AsOf).ThenByDescending(c => c.SetAt)
            .Select(c => new MoneyDto(c.Amount, c.Currency, c.AsOf)).FirstOrDefault();
}
