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
/// Offboarding (docs/lean-pilot/05 §5.6, 04 §4.2): disabling a user revokes every device and offline grant, queues the
/// identity-provider disable + logout-all, and lets other modules flag open work and custody — all in one audited transaction.
/// Nothing is done to the person's phone: the server simply stops accepting it.
/// </summary>
public sealed class UserAdminService(
    AccessDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock, GrantService grants, IEnumerable<IUserOffboardingHandler> handlers)
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var users = await db.Users.AsNoTracking().Include(u => u.RoleAssignments).OrderBy(u => u.Username).ToListAsync(ct);
        var devices = await db.Devices.AsNoTracking().OrderByDescending(d => d.RequestedAt).ToListAsync(ct);
        var tasks = await db.IdpTasks.AsNoTracking().Where(t => t.DoneAt == null).ToListAsync(ct);
        var names = users.ToDictionary(u => u.Id, u => u.DisplayName);
        return users.Select(u =>
        {
            var pending = tasks.Where(t => t.UserId == u.Id).ToList();
            return new UserDto(
                u.Id, u.Username, u.DisplayName, u.StateName, [.. u.ActiveRoles(now)], u.DisabledAt,
                u.DisabledBy is { } by ? names.GetValueOrDefault(by) : null, u.DisableReason,
                devices.Where(d => d.UserId == u.Id).Take(5).Select(d => new UserDeviceDto(d.Id, d.Model, d.Status, d.RequestedAt, d.RevokedAt, d.RevokeReason)).ToList(),
                pending.Count, pending.Any(t => t.Alert), pending.Select(t => t.LastError).FirstOrDefault(e => e is not null));
        }).ToList();
    }

    public Task<Result<Guid>> DisableAsync(CommandEnvelope cmd, Guid userId, string? reason, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, innerCt);
            if (user is null) return Error.NotFound("USER_NOT_FOUND", "User not found.");
            var actorId = cmd.Actor.UserId!.Value;
            var now = clock.UtcNowMicros();
            var disabled = user.Disable(actorId, reason, now);
            if (!disabled.IsSuccess) return disabled.Error!;
            audit.Record(user, "user.disabled", reason!.Trim());

            // 2. every device of the user stops being usable (active → revoked, pending → rejected)
            foreach (var device in await db.Devices.Where(d => d.UserId == userId && (d.Status == DeviceStates.Active || d.Status == DeviceStates.PendingApproval)).ToListAsync(innerCt))
            {
                if (device.IsActive)
                {
                    device.RevokeAsUserDisabled(actorId, now);
                    audit.Record(device, "device.revoked", Device.ReasonUserDisabled);
                }
                else
                {
                    device.Decide(DeviceCommands.RejectDevice, actorId, Device.ReasonUserDisabled, now);
                    audit.Record(device, "device.rejected", Device.ReasonUserDisabled);
                }
            }

            // 3. all offline grants
            await grants.RevokeOpenGrantsAsync(g => g.UserId == userId, GrantRevokeReasons.UserDisabled, actorId, now, innerCt);

            // 4. identity provider: disable + end all sessions (sent after commit, retried)
            db.IdpTasks.Add(new IdpTask(Ids.New(), IdpTaskKinds.DisableUser, userId, user.IdpSubject, now));
            db.IdpTasks.Add(new IdpTask(Ids.New(), IdpTaskKinds.LogoutAll, userId, user.IdpSubject, now));

            // 7–8. open work to reassign, custody to reconcile (other modules, same transaction)
            foreach (var handler in handlers)
            {
                await handler.OnUserDisabledAsync(userId, now, innerCt);
            }

            return user.Id;
        }, ct);

    /// <summary>U2: devices stay revoked; the person registers a phone again and a supervisor approves it.</summary>
    public Task<Result<Guid>> EnableAsync(CommandEnvelope cmd, Guid userId, string? reason, CancellationToken ct) =>
        uow.ExecuteAsync<Guid>(cmd, async innerCt =>
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, innerCt);
            if (user is null) return Error.NotFound("USER_NOT_FOUND", "User not found.");
            var enabled = user.Enable(cmd.Actor.UserId!.Value, reason);
            if (!enabled.IsSuccess) return enabled.Error!;
            audit.Record(user, "user.enabled", reason!.Trim());
            db.IdpTasks.Add(new IdpTask(Ids.New(), IdpTaskKinds.EnableUser, userId, user.IdpSubject, clock.UtcNowMicros()));
            return user.Id;
        }, ct);
}
