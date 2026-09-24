using Mfop.Access.Domain;
using Mfop.Access.Infrastructure;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Mfop.Access.Application;

/// <summary>User records: linked or created on first login, and bootstrap role grants (dev seed). All audited.</summary>
public sealed class UserProvisioning(AccessDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock)
{
    public Task<User?> FindBySubjectAsync(string subject, CancellationToken ct) =>
        db.Users.Include(u => u.RoleAssignments).SingleOrDefaultAsync(u => u.IdpSubject == subject, ct);

    /// <summary>
    /// First login of an identity without a user record: create it with NO roles (roles are granted separately).
    /// The audit actor is the new user (acting through their own authenticated identity).
    /// </summary>
    public async Task<User> ProvisionOnFirstLoginAsync(string subject, string username, string displayName, CancellationToken ct)
    {
        var id = Guid.TryParse(subject, out var parsed) ? parsed : Ids.New();
        var command = CommandEnvelope.For("ProvisionUser", new ActorContext(id, []), "FIRST_LOGIN");
        var result = await uow.ExecuteAsync(command, async innerCt =>
        {
            var existing = await FindBySubjectAsync(subject, innerCt);
            if (existing is not null)
            {
                return Result<User>.Ok(existing);
            }

            var user = new User(id, subject, username, displayName, clock.UtcNowMicros());
            db.Users.Add(user);
            audit.Record(user, "user.provisioned");
            return Result<User>.Ok(user);
        }, ct);

        return result.Value;
    }

    /// <summary>Idempotently ensures a user exists and holds the given roles. Used by the dev bootstrap seed.</summary>
    public async Task<Result<User>> EnsureUserWithRolesAsync(
        Guid id, string username, string displayName, IReadOnlyList<string> roles, ActorContext actor, string reason, CancellationToken ct)
    {
        var command = CommandEnvelope.For("EnsureUserWithRoles", actor, reason);
        return await uow.ExecuteAsync(command, async innerCt =>
        {
            var now = clock.UtcNowMicros();
            var user = await db.Users.Include(u => u.RoleAssignments).SingleOrDefaultAsync(u => u.Id == id, innerCt);
            if (user is null)
            {
                user = new User(id, id.ToString("D"), username, displayName, now);
                db.Users.Add(user);
                audit.Record(user, "user.provisioned");
            }

            foreach (var role in roles)
            {
                var granted = user.GrantRole(role, actor.UserId, reason, now);
                if (!granted.IsSuccess)
                {
                    return Result<User>.Failure(granted.Error!);
                }

                if (granted.Value is { } assignment)
                {
                    db.RoleAssignments.Add(assignment);
                    audit.Record(assignment, "role.granted");
                }
            }

            return Result<User>.Ok(user);
        }, ct);
    }
}
