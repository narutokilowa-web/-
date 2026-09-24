using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Faults.Contracts;
using Mfop.Faults.Domain;
using Mfop.Faults.Infrastructure;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Faults.Application;

public sealed class FaultQueries(FaultsDbContext db, IUserDirectory users, IAssetDirectory assets)
{
    public async Task<IReadOnlyList<FaultDto>> ListAsync(string? state, Guid? assetId, CancellationToken ct)
    {
        var query = db.Faults.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(f => f.State == state);
        if (assetId is { } a) query = query.Where(f => f.AssetId == a);
        var faults = await query.OrderByDescending(f => f.ReportedAt).Take(500).ToListAsync(ct);
        return await ToDtosAsync(faults, includeConfirmations: false, ct);
    }

    public async Task<Result<FaultDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var fault = await db.Faults.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);
        if (fault is null) return Error.NotFound("FAULT_NOT_FOUND", "Fault not found.");
        return (await ToDtosAsync([fault], includeConfirmations: true, ct))[0];
    }

    private async Task<IReadOnlyList<FaultDto>> ToDtosAsync(IReadOnlyList<Fault> faults, bool includeConfirmations, CancellationToken ct)
    {
        var ids = faults.Select(f => f.Id).ToList();
        var confirmations = includeConfirmations
            ? await db.Confirmations.AsNoTracking().Where(c => ids.Contains(c.FaultId)).OrderBy(c => c.At).ToListAsync(ct)
            : [];
        var names = await users.DisplayNamesAsync(
            faults.SelectMany(f => new[] { f.ReportedBy, f.DismissedBy ?? Guid.Empty }).Concat(confirmations.Select(c => c.ConfirmedBy)), ct);
        var assetMap = await assets.GetManyAsync(faults.Select(f => f.AssetId).Distinct().ToList(), ct);
        return faults.Select(f => new FaultDto(
            f.Id, f.AssetId, assetMap.GetValueOrDefault(f.AssetId)?.AssetCode, assetMap.GetValueOrDefault(f.AssetId)?.AddressText,
            f.Category, f.Source, f.ReportedBy, names.GetValueOrDefault(f.ReportedBy, "?"), f.ReportedAt,
            f.PowerAtInspection, f.PowerInfoSource, f.PowerNote, f.ReportedWithoutPower, f.ResponsibleParty, f.Description, f.State, f.WorkOrderId,
            f.DismissReason, f.DismissNote, f.DismissedBy is { } d ? names.GetValueOrDefault(d, "?") : null, f.DismissedAt, f.ConfirmationCount,
            confirmations.Where(c => c.FaultId == f.Id)
                .Select(c => new FaultConfirmationDto(c.Id, names.GetValueOrDefault(c.ConfirmedBy, "?"), c.At, c.PowerAtInspection, c.PowerInfoSource, c.Description))
                .ToList())).ToList();
    }
}

/// <summary>Adds the asset's faults and confirmations to the asset history timeline.</summary>
public sealed class FaultAssetHistory(FaultsDbContext db) : IAssetRelatedHistory
{
    public async Task<IReadOnlyList<(string EntityType, string EntityId)>> RelatedAuditEntitiesAsync(Guid assetId, CancellationToken ct)
    {
        var faultIds = await db.Faults.AsNoTracking().Where(f => f.AssetId == assetId).Select(f => f.Id).ToListAsync(ct);
        var confirmationIds = await db.Confirmations.AsNoTracking().Where(c => faultIds.Contains(c.FaultId)).Select(c => c.Id).ToListAsync(ct);
        return faultIds.Select(id => ("fault", id.ToString("D")))
            .Concat(confirmationIds.Select(id => ("fault_confirmation", id.ToString("D"))))
            .ToList();
    }
}
