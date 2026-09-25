using System.Globalization;
using Mfop.Access.Contracts;
using Mfop.Inventory.Contracts;
using Mfop.Inventory.Domain;
using Mfop.Inventory.Infrastructure;
using Mfop.Kernel.Results;
using Mfop.Rules.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Inventory.Application;

public sealed class InventoryQueries(InventoryDbContext db, IUserDirectory users, InventoryService service, IFlags flags, IPolicies policies, TimeProvider clock)
    : IInventoryLedger
{
    public const int HistoryLimit = 500;

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
        string[] shown = [LocationKinds.Warehouse, LocationKinds.InTransit, LocationKinds.Custody, LocationKinds.Consumed, LocationKinds.CoreBin];
        return balances
            .Where(b => shown.Contains(locations[b.LocationId].Kind))
            .Select(b =>
            {
                var l = locations[b.LocationId];
                var i = items[b.ItemId];
                return new BalanceDto(l.Id, l.Kind, l.Name, l.CustodianUserId, l.CustodianUserId is { } c ? names.GetValueOrDefault(c, "?") : null,
                    i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, b.Quantity);
            })
            .OrderBy(b => Array.IndexOf(shown, b.LocationKind))
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
        var serialsAt = await SerialPositionsAsync(ct);
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

    public Task<Result<Guid>> DeclareCoreReturnAsync(DeclareCoreReturnRequest request, CancellationToken ct) =>
        service.DeclareCoreReturnInCurrentUnitOfWorkAsync(request, ct);

    // ---- M5 documents ----------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<StockIssueDto>> IssuesAsync(string? state, Guid? technicianId, CancellationToken ct)
    {
        var query = db.Issues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(i => i.State == state);
        if (technicianId is { } t) query = query.Where(i => i.TechnicianId == t);
        return await IssueDtosAsync(await query.OrderByDescending(i => i.IssuedAt).Take(HistoryLimit).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<StockReturnDto>> ReturnsAsync(string? state, Guid? technicianId, CancellationToken ct)
    {
        var query = db.Returns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(r => r.State == state);
        if (technicianId is { } t) query = query.Where(r => r.TechnicianId == t);
        return await ReturnDtosAsync(await query.OrderByDescending(r => r.DeclaredAt).Take(HistoryLimit).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<CoreReturnDto>> CoreReturnsAsync(string? state, Guid? technicianId, CancellationToken ct)
    {
        var query = db.CoreReturns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(c => c.State == state);
        if (technicianId is { } t) query = query.Where(c => c.TechnicianId == t);
        return await CoreDtosAsync(await query.OrderByDescending(c => c.CreatedAt).Take(HistoryLimit).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<CoreReturnDto>> CoreReturnsForWorkOrdersAsync(IReadOnlyCollection<Guid> workOrderIds, CancellationToken ct) =>
        await CoreDtosAsync(await db.CoreReturns.AsNoTracking().Where(c => workOrderIds.Contains(c.WorkOrderId)).OrderBy(c => c.CreatedAt).ToListAsync(ct), ct);

    public async Task<TechnicianStockDto> TechnicianStockAsync(Guid userId, CancellationToken ct)
    {
        var issues = await db.Issues.AsNoTracking()
            .Where(i => i.TechnicianId == userId && (i.State == StockIssueStates.Issued || i.State == StockIssueStates.Disputed))
            .OrderBy(i => i.IssuedAt).ToListAsync(ct);
        var returns = await db.Returns.AsNoTracking()
            .Where(r => r.TechnicianId == userId && r.State == StockReturnStates.Declared).OrderBy(r => r.DeclaredAt).ToListAsync(ct);
        var cores = await db.CoreReturns.AsNoTracking()
            .Where(c => c.TechnicianId == userId && c.State != CoreReturnStates.Received && c.State != CoreReturnStates.Waived)
            .OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return new TechnicianStockDto(await IssueDtosAsync(issues, ct), await ReturnDtosAsync(returns, ct), await CoreDtosAsync(cores, ct));
    }

    /// <summary>
    /// Stock history (append-only ledger) filtered by item, location, technician custody, serial, type or document. A serial
    /// also shows where that unit is now and which light it is installed on.
    /// </summary>
    public async Task<StockHistoryDto> HistoryAsync(Guid? itemId, Guid? locationId, Guid? technicianId, string? serial, string? type, Guid? documentId, CancellationToken ct)
    {
        var query = db.Movements.AsNoTracking();
        if (itemId is { } item) query = query.Where(m => m.ItemId == item);
        if (technicianId is { } tech)
        {
            var custody = await db.Locations.AsNoTracking().Where(l => l.Kind == LocationKinds.Custody && l.CustodianUserId == tech).Select(l => (Guid?)l.Id).SingleOrDefaultAsync(ct);
            if (custody is null) return new StockHistoryDto([], null, false);
            locationId = custody;
        }

        if (locationId is { } loc) query = query.Where(m => m.FromLocationId == loc || m.ToLocationId == loc);
        var s = string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
        if (s is not null) query = query.Where(m => m.Serials.Contains(s));
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.Type == type);
        if (documentId is { } doc) query = query.Where(m => m.DocumentId == doc);
        var movements = await query.OrderByDescending(m => m.RecordedAt).ThenByDescending(m => m.Id).Take(HistoryLimit + 1).ToListAsync(ct);
        var truncated = movements.Count > HistoryLimit;
        if (truncated) movements.RemoveAt(movements.Count - 1);

        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var names = await users.DisplayNamesAsync(movements.Select(m => m.ActorId).Distinct(), ct);
        var docIds = movements.Where(m => m.DocumentId is not null).Select(m => m.DocumentId!.Value).Distinct().ToList();
        var codes = await DocumentCodesAsync(docIds, ct);
        var entries = movements.Select(m =>
        {
            var from = locations[m.FromLocationId];
            var to = locations[m.ToLocationId];
            var i = items[m.ItemId];
            return new StockHistoryEntry(m.Id, m.Type, m.OccurredAt, m.RecordedAt, i.Id, i.Sku, i.Unit, m.Quantity, m.Serials, from.Id, from.Kind, from.Name,
                to.Id, to.Kind, to.Name, m.ActorId, names.GetValueOrDefault(m.ActorId, "?"), m.DocumentId,
                m.DocumentId is { } d ? codes.GetValueOrDefault(d) : null, m.WorkOrderId, m.AssetId, m.ReversalOf, m.Note, m.ExceedsCustody, m.SerialNotInCustody);
        }).ToList();

        SerialStatusDto? status = null;
        if (s is not null && itemId is { } forItem && items.TryGetValue(forItem, out var tracked))
        {
            var positions = await SerialPositionsAsync(ct);
            var unit = await db.SerialUnits.AsNoTracking().SingleOrDefaultAsync(u => u.ItemId == forItem && u.Serial == s, ct);
            var at = positions.TryGetValue((forItem, s), out var where) ? locations[where] : null;
            status = new SerialStatusDto(forItem, tracked.Sku, s, at?.Kind, at?.Name, at?.CustodianUserId, unit?.InstalledAssetId, unit?.InstalledAt,
                unit?.RemovedFromAssetId, unit?.RemovedAt, unit?.Historical ?? false);
        }

        return new StockHistoryDto(entries, status, truncated);
    }

    /// <summary>
    /// Warehouse reconciliation (M5): per item, what is where; stored balances against the ledger; in-transit stock against the
    /// open documents; serial counts against balances; the core bin against received cores; custody left with disabled users.
    /// A finding is a signal to look at, never a correction: stock only changes through the commands of its flow.
    /// </summary>
    public async Task<ReconciliationDto> ReconciliationAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var items = await db.Items.AsNoTracking().OrderBy(i => i.Sku).ToListAsync(ct);
        var movements = await db.Movements.AsNoTracking().OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var stored = await db.Balances.AsNoTracking().ToListAsync(ct);
        var findings = new List<ReconciliationFinding>();

        var computed = new Dictionary<(Guid Location, Guid Item), decimal>();
        var serialAt = new Dictionary<(Guid Item, string Serial), Guid>();
        foreach (var m in movements)
        {
            computed[(m.FromLocationId, m.ItemId)] = computed.GetValueOrDefault((m.FromLocationId, m.ItemId)) - m.Quantity;
            computed[(m.ToLocationId, m.ItemId)] = computed.GetValueOrDefault((m.ToLocationId, m.ItemId)) + m.Quantity;
            foreach (var serial in m.Serials) serialAt[(m.ItemId, serial)] = m.ToLocationId;
        }

        var itemNames = items.ToDictionary(i => i.Id, i => i.Sku);
        foreach (var key in computed.Keys.Union(stored.Select(b => (b.LocationId, b.ItemId))).Distinct())
        {
            var c = computed.GetValueOrDefault(key);
            var s = stored.FirstOrDefault(b => b.LocationId == key.Item1 && b.ItemId == key.Item2)?.Quantity ?? 0m;
            if (c != s)
            {
                findings.Add(new("BALANCE_MISMATCH", "CRITICAL",
                    $"{itemNames.GetValueOrDefault(key.Item2, "?")} at {locations[key.Item1].Kind}: stored {Q(s)}, the ledger adds up to {Q(c)}", key.Item2, key.Item1, null));
            }
        }

        // Open documents hold exactly what is in transit.
        var openIssues = await db.Issues.AsNoTracking().Where(i => i.State == StockIssueStates.Issued || i.State == StockIssueStates.Disputed).ToListAsync(ct);
        var openReturns = await db.Returns.AsNoTracking().Where(r => r.State == StockReturnStates.Declared).ToListAsync(ct);
        var openDocs = openIssues.Select(i => i.Id).Concat(openReturns.Select(r => r.Id)).ToHashSet();
        var inTransitOpen = movements.Where(m => m.DocumentId is { } d && openDocs.Contains(d) && (m.Type == MovementTypes.Issue || m.Type == MovementTypes.ReturnDeclare))
            .GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));
        var cores = await db.CoreReturns.AsNoTracking().ToListAsync(ct);

        decimal Sum(Guid itemId, string kind) =>
            computed.Where(kv => kv.Key.Item == itemId && locations[kv.Key.Location].Kind == kind).Sum(kv => kv.Value);

        var rows = new List<ReconciliationItemDto>();
        foreach (var i in items)
        {
            var row = new ReconciliationItemDto(i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, -Sum(i.Id, LocationKinds.Supplier), Sum(i.Id, LocationKinds.Warehouse),
                Sum(i.Id, LocationKinds.InTransit), Sum(i.Id, LocationKinds.Custody), Sum(i.Id, LocationKinds.Consumed), Sum(i.Id, LocationKinds.CoreBin),
                cores.Where(c => c.ItemId == i.Id && c.State == CoreReturnStates.Received).Sum(c => c.Quantity), inTransitOpen.GetValueOrDefault(i.Id));
            rows.Add(row);
            if (row.InTransit != row.InTransitOpenDocuments)
            {
                findings.Add(new("IN_TRANSIT_MISMATCH", "CRITICAL", $"{i.Sku}: {Q(row.InTransit)} in transit, open issues/returns hold {Q(row.InTransitOpenDocuments)}", i.Id, null, null));
            }

            if (row.CoreBin != row.CoresExpected)
            {
                findings.Add(new("CORE_BIN_MISMATCH", "CRITICAL", $"{i.Sku}: core bin holds {Q(row.CoreBin)}, received core returns add up to {Q(row.CoresExpected)}", i.Id, null, null));
            }

            foreach (var loc in locations.Values.Where(l => l.Kind is LocationKinds.Warehouse or LocationKinds.InTransit or LocationKinds.Custody))
            {
                var q = computed.GetValueOrDefault((loc.Id, i.Id));
                if (q < 0)
                {
                    // Custody can go below zero when consumption was applied beyond the known custody (flagged at the time).
                    findings.Add(new("NEGATIVE_BALANCE", loc.Kind == LocationKinds.Custody ? "INFO" : "CRITICAL",
                        $"{i.Sku} at {loc.Kind}: {Q(q)}", i.Id, loc.Id, loc.CustodianUserId));
                }

                if (i.SerialTracked)
                {
                    var serials = serialAt.Count(kv => kv.Key.Item == i.Id && kv.Value == loc.Id);
                    if (q >= 0 && serials != q)
                    {
                        findings.Add(new("SERIAL_COUNT_MISMATCH", "WARNING", $"{i.Sku} at {loc.Kind}: balance {Q(q)}, serials located there {serials}", i.Id, loc.Id,
                            loc.CustodianUserId));
                    }
                }
            }
        }

        // Custody per technician, with open documents; custody left with a disabled user is a finding.
        var custodians = locations.Values.Where(l => l.Kind == LocationKinds.Custody && l.CustodianUserId is not null).ToList();
        var userIds = custodians.Select(l => l.CustodianUserId!.Value).Concat(openIssues.Select(i => i.TechnicianId)).Concat(cores.Select(c => c.TechnicianId)).Distinct().ToList();
        var names = await users.DisplayNamesAsync(userIds, ct);
        var inactive = await users.InactiveAsync(userIds, ct);
        var holders = new List<CustodyHolderDto>();
        foreach (var userId in userIds)
        {
            var custody = custodians.FirstOrDefault(l => l.CustodianUserId == userId);
            var held = custody is null ? [] : items.Select(i =>
            {
                var q = computed.GetValueOrDefault((custody.Id, i.Id));
                var serials = serialAt.Where(kv => kv.Key.Item == i.Id && kv.Value == custody.Id).Select(kv => kv.Key.Serial).Order(StringComparer.Ordinal).ToList();
                return new CustodyItem(i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, i.SerialTracked, q, serials);
            }).Where(c => c.Quantity != 0).ToList();
            var openCores = cores.Count(c => c.TechnicianId == userId && c.State is CoreReturnStates.Open or CoreReturnStates.DeclaredWithPart or CoreReturnStates.DeclaredUnrecoverable);
            var active = !inactive.Contains(userId);
            holders.Add(new CustodyHolderDto(userId, names.GetValueOrDefault(userId, "?"), active, held,
                openIssues.Count(i => i.TechnicianId == userId), openReturns.Count(r => r.TechnicianId == userId), openCores));
            if (!active && (held.Count > 0 || openCores > 0))
            {
                findings.Add(new("CUSTODY_OF_DISABLED_USER", "WARNING",
                    $"{names.GetValueOrDefault(userId, "?")} is disabled and still holds {string.Join(", ", held.Select(h => $"{h.Sku}:{Q(h.Quantity)}"))}" +
                    (openCores > 0 ? $"; {openCores} core return(s) not finished" : string.Empty), null, custody?.Id, userId));
            }
        }

        var ackHours = await policies.IntAsync("issue_ack_warning_hours", ct);
        var overdueDays = await policies.IntAsync("core_return_overdue_days", ct);
        var open = new OpenDocumentsDto(
            openIssues.Count(i => i.State == StockIssueStates.Issued),
            openIssues.Count(i => i.State == StockIssueStates.Issued && i.IssuedAt.AddHours(ackHours) <= now),
            openIssues.Count(i => i.State == StockIssueStates.Disputed),
            openReturns.Count,
            cores.Count(c => c.State == CoreReturnStates.Open),
            cores.Count(c => c.State == CoreReturnStates.DeclaredWithPart),
            cores.Count(c => c.State == CoreReturnStates.DeclaredWithPart && c.DeclaredAt is { } d && d.AddDays(overdueDays) <= now),
            cores.Count(c => c.State == CoreReturnStates.DeclaredUnrecoverable));
        return new ReconciliationDto(now, findings.All(f => f.Severity == "INFO"), movements.Count, rows,
            holders.OrderBy(h => h.Name, StringComparer.Ordinal).ToList(), open, findings);
    }

    // ---- mapping ---------------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<StockIssueDto>> IssueDtosAsync(List<StockIssue> issues, CancellationToken ct)
    {
        if (issues.Count == 0) return [];
        var ids = issues.Select(i => i.Id).ToList();
        var lines = await LinesAsync(ids, MovementTypes.Issue, ct);
        var names = await users.DisplayNamesAsync(issues.SelectMany(i => new[] { i.TechnicianId, i.IssuedBy }.Concat(i.CancelledBy is { } c ? [c] : [])).Distinct(), ct);
        var docFlags = await FlagsAsync("stock_issue", ids, ct);
        var hours = await policies.IntAsync("issue_ack_warning_hours", ct);
        var now = clock.GetUtcNow();
        return issues.Select(i => new StockIssueDto(i.Id, i.Code, i.State, i.TechnicianId, names.GetValueOrDefault(i.TechnicianId, "?"), i.IssuedBy,
            names.GetValueOrDefault(i.IssuedBy, "?"), i.IssuedAt, i.Note, i.ReferenceWorkOrderId, i.RequestedBy, i.RespondedAt, i.DisputeReason, i.DisputeEvidenceId,
            i.CancelledAt, i.CancelledBy is { } c ? names.GetValueOrDefault(c, "?") : null, i.CancelReason,
            i.State == StockIssueStates.Issued && i.IssuedAt.AddHours(hours) <= now, lines.GetValueOrDefault(i.Id, []), docFlags.GetValueOrDefault(i.Id, []))).ToList();
    }

    private async Task<IReadOnlyList<StockReturnDto>> ReturnDtosAsync(List<StockReturn> returns, CancellationToken ct)
    {
        if (returns.Count == 0) return [];
        var ids = returns.Select(r => r.Id).ToList();
        var declared = await LinesAsync(ids, MovementTypes.ReturnDeclare, ct);
        var offboarding = await LinesAsync(returns.Where(r => r.Kind == StockReturnKinds.Offboarding).Select(r => r.Id).ToList(), MovementTypes.ReturnReceive, ct);
        var names = await users.DisplayNamesAsync(returns.SelectMany(r => new[] { r.TechnicianId, r.DeclaredBy }.Concat(r.DecidedBy is { } d ? [d] : [])).Distinct(), ct);
        var docFlags = await FlagsAsync("stock_return", ids, ct);
        return returns.Select(r => new StockReturnDto(r.Id, r.Code, r.Kind, r.State, r.TechnicianId, names.GetValueOrDefault(r.TechnicianId, "?"), r.DeclaredBy,
            names.GetValueOrDefault(r.DeclaredBy, "?"), r.DeclaredAt, r.Note, r.DecidedAt, r.DecidedBy is { } d ? names.GetValueOrDefault(d, "?") : null, r.RejectReason,
            r.Kind == StockReturnKinds.Offboarding ? offboarding.GetValueOrDefault(r.Id, []) : declared.GetValueOrDefault(r.Id, []),
            docFlags.GetValueOrDefault(r.Id, []))).ToList();
    }

    private async Task<IReadOnlyList<CoreReturnDto>> CoreDtosAsync(List<CoreReturn> cores, CancellationToken ct)
    {
        if (cores.Count == 0) return [];
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        var names = await users.DisplayNamesAsync(cores.SelectMany(c => new[] { c.TechnicianId }
            .Concat(c.ReceivedBy is { } r ? [r] : []).Concat(c.WaivedBy is { } w ? [w] : [])).Distinct(), ct);
        var docFlags = await FlagsAsync("core_return", cores.Select(c => c.Id).ToList(), ct);
        var days = await policies.IntAsync("core_return_overdue_days", ct);
        var now = clock.GetUtcNow();
        return cores.Select(c =>
        {
            var i = items[c.ItemId];
            return new CoreReturnDto(c.Id, c.Code, c.State, c.ConsumeMovementId, c.WorkOrderId, c.WorkOrderCode, c.AssetId, c.AssetCode, i.Id, i.Sku, i.NameAr, i.NameEn, i.SerialTracked,
                c.Quantity, c.TechnicianId, names.GetValueOrDefault(c.TechnicianId, "?"), c.InstalledSerial, c.ExpectedSerial, c.CreatedAt, c.DeclaredAt,
                c.DeclaredSerial, c.DeclaredReason, c.EvidenceId, c.ReceivedAt, c.ReceivedBy is { } r ? names.GetValueOrDefault(r, "?") : null, c.ReceivedSerial,
                c.SerialMismatch, c.NotPresentedReason, c.WaivedAt, c.WaivedBy is { } w ? names.GetValueOrDefault(w, "?") : null, c.WaiveReason,
                c.State == CoreReturnStates.DeclaredWithPart && c.DeclaredAt is { } d && d.AddDays(days) <= now, docFlags.GetValueOrDefault(c.Id, []));
        }).ToList();
    }

    private async Task<Dictionary<Guid, IReadOnlyList<DocumentLineDto>>> LinesAsync(IReadOnlyCollection<Guid> documentIds, string type, CancellationToken ct)
    {
        if (documentIds.Count == 0) return [];
        var movements = await db.Movements.AsNoTracking().Where(m => m.DocumentId != null && documentIds.Contains(m.DocumentId.Value) && m.Type == type)
            .OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
        return movements.GroupBy(m => m.DocumentId!.Value).ToDictionary(g => g.Key, g => (IReadOnlyList<DocumentLineDto>)g.Select(m =>
        {
            var i = items[m.ItemId];
            return new DocumentLineDto(m.Id, i.Id, i.Sku, i.NameAr, i.NameEn, i.Unit, i.SerialTracked, m.Quantity, m.Serials);
        }).ToList());
    }

    private async Task<Dictionary<Guid, IReadOnlyList<DocumentFlagDto>>> FlagsAsync(string subjectType, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        (await flags.ForSubjectsAsync(subjectType, ids, ct)).GroupBy(f => f.SubjectId)
        .ToDictionary(g => g.Key, g => (IReadOnlyList<DocumentFlagDto>)g.Select(f => new DocumentFlagDto(f.Id, f.RuleCode, f.Severity, f.State, f.CreatedAt)).ToList());

    private async Task<Dictionary<Guid, string>> DocumentCodesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var codes = new Dictionary<Guid, string>();
        foreach (var x in await db.Issues.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Code }).ToListAsync(ct)) codes[x.Id] = x.Code;

        foreach (var x in await db.Returns.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Code }).ToListAsync(ct)) codes[x.Id] = x.Code;
        foreach (var x in await db.CoreReturns.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Code }).ToListAsync(ct)) codes[x.Id] = x.Code;
        return codes;
    }

    private async Task<Dictionary<(Guid Item, string Serial), Guid>> SerialPositionsAsync(CancellationToken ct)
    {
        var serialMovements = await db.Movements.AsNoTracking().Where(m => m.Serials.Length > 0).OrderBy(m => m.RecordedAt).ThenBy(m => m.Id).ToListAsync(ct);
        var serialsAt = new Dictionary<(Guid Item, string Serial), Guid>();
        foreach (var m in serialMovements)
        {
            foreach (var s in m.Serials) serialsAt[(m.ItemId, s)] = m.ToLocationId;
        }

        return serialsAt;
    }

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

    private static string Q(decimal q) => q.ToString("0.###", CultureInfo.InvariantCulture);
}
