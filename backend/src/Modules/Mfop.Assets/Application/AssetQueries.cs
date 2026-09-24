using Mfop.Access.Contracts;
using Mfop.Assets.Contracts;
using Mfop.Assets.Domain;
using Mfop.Assets.Infrastructure;
using Mfop.Audit.Contracts;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Mfop.Assets.Application;

public sealed class AssetQueries(AssetsDbContext db, IUserDirectory users, IAuditQueries auditQueries, IOptions<TagOptions> tagOptions)
{
    public async Task<IReadOnlyList<RegistrationDto>> ListRegistrationsAsync(ActorContext actor, string? state, CancellationToken ct)
    {
        var query = db.Registrations.AsNoTracking();
        if (!actor.HasRole(RoleCodes.Approver) && !actor.HasRole(RoleCodes.Auditor))
        {
            query = query.Where(r => r.RegistrarId == actor.UserId); // registrars see only their own
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(r => r.State == state);
        }

        var registrations = await query.OrderByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
        var assetIds = registrations.Select(r => r.AssetId).ToList();
        var assets = await db.StreetLights.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var names = await users.DisplayNamesAsync(registrations.SelectMany(r => new[] { r.RegistrarId, r.DecidedBy ?? Guid.Empty }), ct);
        return registrations.Select(r => ToDto(r, assets[r.AssetId], names, actor)).ToList();
    }

    public async Task<Result<RegistrationDto>> GetRegistrationAsync(ActorContext actor, Guid id, CancellationToken ct)
    {
        var registration = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        if (registration is null || (!actor.HasRole(RoleCodes.Approver) && !actor.HasRole(RoleCodes.Auditor) && registration.RegistrarId != actor.UserId))
        {
            return Error.NotFound("REGISTRATION_NOT_FOUND", "Registration not found.");
        }

        var asset = await db.StreetLights.AsNoTracking().SingleAsync(a => a.Id == registration.AssetId, ct);
        var names = await users.DisplayNamesAsync([registration.RegistrarId, registration.DecidedBy ?? Guid.Empty], ct);
        return ToDto(registration, asset, names, actor);
    }

    public async Task<IReadOnlyList<AssetListItem>> ListAssetsAsync(string? search, CancellationToken ct)
    {
        var query = db.StreetLights.AsNoTracking().Where(a => a.LifecycleState == LifecycleStates.Active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            query = query.Where(a => a.AssetCode!.Contains(s) || (a.AddressText != null && a.AddressText.Contains(search.Trim())));
        }

        return await query.OrderBy(a => a.AssetCode).Take(500)
            .Select(a => new AssetListItem(a.Id, a.AssetCode, a.LifecycleState, a.OperationalStatus, a.PowerSource, a.AddressText, a.Latitude, a.Longitude))
            .ToListAsync(ct);
    }

    public async Task<Result<AssetDto>> GetAssetAsync(Guid id, CancellationToken ct)
    {
        var asset = await db.StreetLights.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null)
        {
            return Error.NotFound("ASSET_NOT_FOUND", "Asset not found.");
        }

        var tag = asset.CurrentTagId is { } tagId ? await db.Tags.AsNoTracking().SingleAsync(t => t.Id == tagId, ct) : null;
        var versions = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == id).OrderBy(v => v.VersionNo).ToListAsync(ct);
        var registrationId = await db.Registrations.AsNoTracking().Where(r => r.AssetId == id).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        var names = await users.DisplayNamesAsync(versions.Select(v => v.ApprovedBy), ct);

        return new AssetDto(
            asset.Id, asset.AssetCode, asset.LifecycleState, asset.OperationalStatus, asset.ToAttributes(), asset.CaptureChannel,
            tag is null ? null : new TagDto(tag.Code, tag.CheckCode, tag.HumanCode, $"{tagOptions.Value.BaseUrl.TrimEnd('/')}/{tag.HumanCode}"),
            asset.CurrentVersionNo,
            versions.Select(v => new AssetVersionDto(v.VersionNo, v.SnapshotHash, v.ApprovedBy, names.GetValueOrDefault(v.ApprovedBy, "?"), v.ApprovedAt, v.Reason)).ToList(),
            registrationId);
    }

    /// <summary>
    /// Timeline of everything recorded about the asset: the street light itself, its registration(s),
    /// its tag(s) and its approved versions — straight from the audit log (old → new values, actor, time, reason, hash).
    /// </summary>
    public async Task<Result<IReadOnlyList<HistoryEntry>>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.StreetLights.AnyAsync(a => a.Id == id, ct))
        {
            return Error.NotFound("ASSET_NOT_FOUND", "Asset not found.");
        }

        var keys = new List<(string, string)> { ("street_light", id.ToString("D")) };
        keys.AddRange((await db.Registrations.Where(r => r.AssetId == id).Select(r => r.Id).ToListAsync(ct)).Select(x => ("registration", x.ToString("D"))));
        keys.AddRange((await db.Tags.Where(t => t.BoundAssetId == id).Select(t => t.Id).ToListAsync(ct)).Select(x => ("tag", x.ToString("D"))));
        keys.AddRange((await db.AssetVersions.Where(v => v.AssetId == id).Select(v => v.Id).ToListAsync(ct)).Select(x => ("asset_version", x.ToString("D"))));

        var events = await auditQueries.ForEntitiesAsync(keys, ct);
        var names = await users.DisplayNamesAsync(events.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!.Value), ct);
        return events.Select(e => new HistoryEntry(
            e.Seq, e.OccurredAt, e.ActorUserId,
            e.ActorUserId is { } u ? names.GetValueOrDefault(u, "?") : "SYSTEM",
            e.ActorRoles, e.Action, e.EntityType, e.EntityId, e.Changes, e.Facts, e.Reason, e.Hash)).ToList();
    }

    public async Task<Result<Guid>> ResolveTagAsync(string token, CancellationToken ct)
    {
        var parsed = TagCodes.Parse(token);
        if (parsed is { } p && tagOptions.Value.CheckKey is { Length: > 0 } key && TagCodes.CheckCode(p.Code, key) == p.Check)
        {
            var tag = await db.Tags.AsNoTracking().SingleOrDefaultAsync(t => t.Code == p.Code && t.Status == "BOUND", ct);
            if (tag?.BoundAssetId is { } assetId)
            {
                return assetId;
            }
        }

        return Error.NotFound("TAG_NOT_FOUND", "No active asset for this tag.");
    }

    private static RegistrationDto ToDto(Registration r, StreetLight a, IReadOnlyDictionary<Guid, string> names, ActorContext actor) => new(
        r.Id, r.AssetId, r.State, r.Reason, r.ProjectRef, r.CaptureChannel, r.RegistrarId, names.GetValueOrDefault(r.RegistrarId, "?"),
        r.CreatedAt, r.SubmittedAt, r.DecidedBy, r.DecidedBy is { } d ? names.GetValueOrDefault(d, "?") : null, r.DecidedAt,
        r.DecisionNote, r.ReturnCount, a.ToAttributes(), a.AssetCode, AllowedCommands(r, actor));

    /// <summary>Commands this actor may issue now (drives which buttons the web shows; the server re-checks everything).</summary>
    private static IReadOnlyList<string> AllowedCommands(Registration r, ActorContext actor) =>
        AssetStateMachines.Registration.Transitions
            .Where(t => t.From.Contains(r.State))
            .Where(t => (t.Actors.Contains("registrar") && r.RegistrarId == actor.UserId && actor.HasRole(RoleCodes.Registrar))
                        || (t.Actors.Contains("APPROVER") && actor.HasRole(RoleCodes.Approver) && r.RegistrarId != actor.UserId))
            .Select(t => t.Command)
            .Distinct()
            .ToList();
}
