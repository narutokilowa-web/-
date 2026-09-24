using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

public sealed class AssigneeDirectory(AccessDbContext db, TimeProvider clock) : IAssigneeDirectory
{
    public async Task<IReadOnlyList<AssignableTechnician>> TechniciansAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var users = await db.Users.AsNoTracking().Include(u => u.RoleAssignments).Where(u => u.Status == UserStatus.Active).ToListAsync(ct);
        var technicians = users.Where(u => u.ActiveRoles(now).Contains(RoleCodes.Technician)).ToList();
        var ids = technicians.Select(u => u.Id).ToList();
        var withDevice = await db.Devices.AsNoTracking().Where(d => ids.Contains(d.UserId) && d.Status == DeviceStates.Active).Select(d => d.UserId).ToListAsync(ct);
        return technicians
            .Select(u => new AssignableTechnician(u.Id, u.DisplayName, u.Username, withDevice.Contains(u.Id)))
            .OrderBy(t => t.Username, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<AssigneeCheck> CheckAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.RoleAssignments).SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return new AssigneeCheck(false, false, false, false);
        }

        var hasDevice = await db.Devices.AnyAsync(d => d.UserId == userId && d.Status == DeviceStates.Active, ct);
        return new AssigneeCheck(true, user.Status == UserStatus.Active, user.ActiveRoles(clock.GetUtcNow()).Contains(RoleCodes.Technician), hasDevice);
    }
}
