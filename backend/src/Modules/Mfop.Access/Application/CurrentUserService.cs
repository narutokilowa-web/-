using Mfop.Access.Contracts;
using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

/// <summary>Resolves the authenticated identity (token "sub") to our user record and its active roles.</summary>
public sealed class CurrentUserService(AccessDbContext db, TimeProvider clock)
{
    public async Task<CurrentUserResult> ResolveAsync(string? idpSubject, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idpSubject))
        {
            return CurrentUserResult.NotProvisioned();
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .SingleOrDefaultAsync(u => u.IdpSubject == idpSubject, ct);

        if (user is null)
        {
            return CurrentUserResult.NotProvisioned();
        }

        if (user.Status != UserStatus.Active)
        {
            return CurrentUserResult.Disabled();
        }

        var now = clock.GetUtcNow();
        return CurrentUserResult.Ok(new CurrentUserDto(user.Id, user.Username, user.DisplayName, [.. user.ActiveRoles(now)], "database"));
    }
}
