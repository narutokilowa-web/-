using Mfop.Access.Contracts;
using Mfop.Access.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

public sealed class UserDirectory(AccessDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    public async Task<IReadOnlySet<Guid>> InactiveAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return (await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Status != Domain.UserStatus.Active).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
    }
}
