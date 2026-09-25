using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

/// <summary>
/// Device trust (BYOD): registration by the owner, approval / rejection / revocation by a supervisor or admin.
/// Revocation is server-side only; the API rejects the device on its next request (<see cref="Api.ActorResolutionMiddleware"/>).
/// </summary>
public sealed class DeviceService(
    AccessDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, IEnumerable<IDeviceSyncInfo> syncInfo, GrantService grants)
{
    public static readonly IReadOnlySet<string> Platforms = new HashSet<string>(StringComparer.Ordinal) { "ANDROID" };

    public Task<Result<Guid>> RegisterAsync(CommandEnvelope cmd, RegisterDeviceRequest request, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var invalid = Validate(request);
            if (invalid is not null) return invalid;

            var userId = cmd.Actor.UserId!.Value;
            var now = clock.UtcNowMicros();
            foreach (var pending in await db.Devices.Where(d => d.UserId == userId && d.Status == DeviceStates.PendingApproval).ToListAsync(innerCt))
            {
                pending.RejectAsSuperseded(now);
                audit.Record(pending, "device.superseded", Device.ReasonSuperseded);
            }

            var device = new Device(Ids.New(), userId, request.Platform, request.Model.Trim(), request.OsVersion.Trim(), request.AppVersion.Trim(), now,
                request.PublicKey!.Trim());
            db.Devices.Add(device);
            audit.Record(device, "device.registered");
            return device.Id;
        }, ct);

    public Task<Result<Guid>> ApproveAsync(CommandEnvelope cmd, Guid id, string? reason, CancellationToken ct) =>
        DecideAsync(cmd, id, DeviceCommands.ApproveDevice, reason, "device.approved", async (device, now, innerCt) =>
        {
            var others = await db.Devices.Where(d => d.UserId == device.UserId && d.Id != device.Id && d.Status == DeviceStates.Active).ToListAsync(innerCt);
            foreach (var old in others)
            {
                old.RevokeAsReplaced(cmd.Actor.UserId!.Value, now);
                audit.Record(old, "device.revoked", Device.ReasonReplaced);
                await grants.RevokeOpenGrantsAsync(g => g.DeviceId == old.Id, GrantRevokeReasons.DeviceRevoked, cmd.Actor.UserId, now, innerCt);
            }
        }, ct);

    public Task<Result<Guid>> RejectAsync(CommandEnvelope cmd, Guid id, string? reason, CancellationToken ct) =>
        DecideAsync(cmd, id, DeviceCommands.RejectDevice, reason, "device.rejected", null, ct);

    /// <summary>
    /// D4 (docs/lean-pilot/04 §4.1): the device's offline grants are revoked and the owner's field-app sessions are ended at the
    /// identity provider (after commit, retried). The next request from the phone is refused.
    /// </summary>
    public Task<Result<Guid>> RevokeAsync(CommandEnvelope cmd, Guid id, string? reason, CancellationToken ct) =>
        DecideAsync(cmd, id, DeviceCommands.RevokeDevice, reason, "device.revoked", async (device, now, innerCt) =>
        {
            await grants.RevokeOpenGrantsAsync(g => g.DeviceId == device.Id, GrantRevokeReasons.DeviceRevoked, cmd.Actor.UserId, now, innerCt);
            var owner = await db.Users.AsNoTracking().SingleAsync(u => u.Id == device.UserId, innerCt);
            db.IdpTasks.Add(new IdpTask(Ids.New(), IdpTaskKinds.LogoutMobile, owner.Id, owner.IdpSubject, now));
        }, ct);

    public Task<Device?> FindAsync(Guid id, CancellationToken ct) => db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(string? status, CancellationToken ct)
    {
        var query = db.Devices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(d => d.Status == status);
        }

        var devices = await query.OrderByDescending(d => d.RequestedAt).Take(500).ToListAsync(ct);
        var userIds = devices.SelectMany(d => new[] { d.UserId, d.DecidedBy ?? Guid.Empty, d.RevokedBy ?? Guid.Empty }).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var sync = new Dictionary<Guid, DeviceSyncSnapshot>();
        foreach (var provider in syncInfo)
        {
            foreach (var (k, v) in await provider.ForDevicesAsync(devices.Select(d => d.Id).ToList(), ct))
            {
                sync[k] = v;
            }
        }

        string? Name(Guid? id) => id is { } x && users.TryGetValue(x, out var u) ? u.DisplayName : null;
        return devices.Select(d =>
        {
            var s = sync.GetValueOrDefault(d.Id);
            return new DeviceDto(
                d.Id, d.UserId, Name(d.UserId) ?? "?", users.TryGetValue(d.UserId, out var owner) ? owner.Username : "?",
                d.Platform, d.Model, d.OsVersion, d.AppVersion, d.Status, d.RequestedAt,
                Name(d.DecidedBy), d.DecidedAt, d.DecisionReason, Name(d.RevokedBy), d.RevokedAt, d.RevokeReason, d.PublicKey is not null,
                s?.LastPullAt, s?.LastPushAt, s?.LastReportedPendingCommands);
        }).ToList();
    }

    private Task<Result<Guid>> DecideAsync(
        CommandEnvelope cmd, Guid id, string command, string? reason, string action,
        Func<Device, DateTimeOffset, CancellationToken, Task>? effects, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, innerCt);
            if (device is null) return Error.NotFound(DeviceErrorCodes.DeviceNotFound, "Device not found.");

            var now = clock.UtcNowMicros();
            var decided = device.Decide(command, cmd.Actor.UserId!.Value, reason, now);
            if (!decided.IsSuccess) return decided.Error!;

            audit.Record(device, action, reason);
            if (effects is not null)
            {
                await effects(device, now, innerCt);
            }

            return device.Id;
        }, ct);

    private static Error? Validate(RegisterDeviceRequest r)
    {
        var details = new Dictionary<string, string>();
        if (r.Platform is null || !Platforms.Contains(r.Platform)) details["platform"] = "must be ANDROID";
        if (string.IsNullOrWhiteSpace(r.Model) || r.Model.Length > 100) details["model"] = "required, at most 100 characters";
        if (string.IsNullOrWhiteSpace(r.OsVersion) || r.OsVersion.Length > 50) details["osVersion"] = "required, at most 50 characters";
        if (string.IsNullOrWhiteSpace(r.AppVersion) || r.AppVersion.Length > 30) details["appVersion"] = "required, at most 30 characters";
        if (!DeviceKeys.IsValidPublicKey(r.PublicKey)) details["publicKey"] = "required: EC P-256 public key (SubjectPublicKeyInfo, base64) from the device keystore";
        return details.Count == 0 ? null : Error.Validation(ErrorCodes.ValidationFailed, "Invalid device registration.", details);
    }
}
