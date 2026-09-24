using Mfop.Access.Contracts;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;

namespace Mfop.Access.Domain;

public enum UserStatus
{
    Active = 1,
    Disabled = 2,
}

/// <summary>
/// A person known to the platform. Linked to the identity provider by <see cref="IdpSubject"/> (Keycloak "sub").
/// Users are disabled, never deleted.
/// </summary>
public sealed class User : IAuditable
{
    private User() { }

    public User(Guid id, string idpSubject, string username, string displayName, DateTimeOffset createdAt)
    {
        Id = id;
        IdpSubject = idpSubject;
        Username = username;
        DisplayName = displayName;
        Status = UserStatus.Active;
        CreatedAt = createdAt;
    }

    public string AuditEntityType => "user";

    public Guid Id { get; private set; }
    public string IdpSubject { get; private set; } = string.Empty;
    public string Username { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int Version { get; private set; }

    public List<RoleAssignment> RoleAssignments { get; private set; } = [];

    public IEnumerable<string> ActiveRoles(DateTimeOffset now) =>
        RoleAssignments.Where(r => r.IsActive(now)).Select(r => r.Role).Distinct().Order(StringComparer.Ordinal);

    /// <summary>Grants a role unless the user already holds it or it conflicts with a held role (static SoD).</summary>
    public Result<RoleAssignment?> GrantRole(string role, Guid? grantedBy, string reason, DateTimeOffset now)
    {
        if (!RoleCodes.All.Contains(role))
        {
            return Error.Validation("UNKNOWN_ROLE", $"Unknown role '{role}'.");
        }

        if (grantedBy == Id)
        {
            return Error.Sod("A user cannot grant roles to themselves.");
        }

        var held = ActiveRoles(now).ToList();
        if (held.Contains(role))
        {
            return Result<RoleAssignment?>.Ok(null);
        }

        var conflict = StaticSeparationOfDuties.ConflictWith(role, held);
        if (conflict is not null)
        {
            return new Error("SOD_ROLE_CONFLICT", $"Role {role} cannot be combined with {conflict}.", ErrorKind.SodViolation);
        }

        var assignment = new RoleAssignment(Ids.New(), Id, role, grantedBy, reason, now);
        RoleAssignments.Add(assignment);
        return assignment;
    }
}
