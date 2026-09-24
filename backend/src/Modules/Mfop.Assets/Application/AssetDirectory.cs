using Mfop.Assets.Contracts;
using Mfop.Assets.Domain;
using Mfop.Assets.Infrastructure;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Assets.Application;

public sealed class AssetDirectory(AssetsDbContext db, AssetQueries queries) : IAssetDirectory
{
    public async Task<AssetSummary?> GetAsync(Guid assetId, CancellationToken ct) =>
        (await GetManyAsync([assetId], ct)).GetValueOrDefault(assetId);

    public async Task<IReadOnlyDictionary<Guid, AssetSummary>> GetManyAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct) =>
        await db.StreetLights.AsNoTracking()
            .Where(a => assetIds.Contains(a.Id))
            .Select(a => new AssetSummary(a.Id, a.AssetCode, a.LifecycleState, a.OperationalStatus, a.PowerSource, a.ResponsibleParty,
                a.ResponsibleEntityName, a.AddressText, a.Latitude, a.Longitude))
            .ToDictionaryAsync(a => a.Id, ct);

    public async Task<Guid?> ResolveTagAsync(string token, CancellationToken ct)
    {
        var resolved = await queries.ResolveTagAsync(token, ct);
        return resolved.IsSuccess ? resolved.Value : null;
    }

    public async Task<IReadOnlyList<HistoryEntry>> RecentHistoryAsync(Guid assetId, int take, CancellationToken ct)
    {
        var history = await queries.HistoryAsync(assetId, ct);
        return history.IsSuccess ? history.Value.OrderByDescending(e => e.Seq).Take(take).ToList() : [];
    }
}

public sealed class AssetStatusEvents(AssetsDbContext db, IAuditTrail audit, TimeProvider clock) : IAssetStatusEvents
{
    public async Task<AssetStatusChange?> ApplyAsync(Guid assetId, string statusCommand, string causeType, Guid causeId, CancellationToken ct)
    {
        var asset = await db.StreetLights.SingleAsync(a => a.Id == assetId, ct);
        var from = asset.OperationalStatus;
        var transition = asset.ApplyStatusEvent(statusCommand);
        if (transition is null)
        {
            return null;
        }

        var statusEvent = new AssetStatusEvent(Ids.New(), asset.Id, transition.Id, from, transition.To, causeType, causeId, clock.UtcNowMicros());
        db.AssetStatusEvents.Add(statusEvent);
        var facts = new Dictionary<string, object?> { ["transition"] = transition.Id, ["cause_type"] = causeType, ["cause_id"] = causeId.ToString("D") };
        audit.Record(asset, "street_light.operational_status_changed", extra: facts);
        audit.Record(statusEvent, "asset_status_event.recorded");
        return new AssetStatusChange(transition.Id, from, transition.To);
    }
}
