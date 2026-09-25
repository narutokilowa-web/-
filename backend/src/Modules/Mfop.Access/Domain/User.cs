using Mfop.Access.Contracts;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Results;
using Mfop.Kernel.StateMachines;

namespace Mfop.Access.Domain;

public static class UserStates
{
    public const string Active = "ACTIVE", Disabled = "DISABLED";
}

public static class UserCommands
{
    public const string ProvisionUser = nameof(ProvisionUser);
    public const string DisableUser = nameof(DisableUser);
    public const string EnableUser = nameof(EnableUser);
}

/// <summary>Transition table. MUST equal docs/state-machines/user.yaml (StateMachineConformanceTests).</summary>
public static class UserStateMachine
{
    public static readonly StateMachine Machine = new(
        "User",
        [UserStates.Active, UserStates.Disabled],
        UserStates.Active,
        [],
        [
            new("U0", [StateMachine.Start], UserCommands.ProvisionUser, UserStates.Active, ["system"]),
            new("U1", [UserStates.Active], UserCommands.DisableUser, UserStates.Disabled, ["ADMIN"]),
            new("U2", [UserStates.Disabled], UserCommands.EnableUser, UserStates.Active, ["ADMIN"]),
        ]);
}

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
    public DateTimeOffset? DisabledAt { get; private set; }
    public Guid? DisabledBy { get; private set; }
    public string? DisableReason { get; private set; }
    public int Version { get; private set; }

    /// <summary>
    /// U1 DisableUser (docs/lean-pilot/04 §4.2): offboarding. The caller cascades devices, grants, identity provider and
    /// work (05 §5.6). Nobody disables themselves.
    /// </summary>
    public Result<bool> Disable(Guid actorId, string? reason, DateTimeOffset now)
    {
        var t = UserStateMachine.Machine.Resolve(StateName, UserCommands.DisableUser);
        if (!t.IsSuccess) return t.Error!;
        if (actorId == Id) return Error.Sod("You cannot disable your own account.");
        if (string.IsNullOrWhiteSpace(reason)) return Error.Validation("REASON_REQUIRED", "A reason is required.");
        Status = UserStatus.Disabled;
        DisabledAt = now;
        DisabledBy = actorId;
        DisableReason = reason.Trim();
        return true;
    }

    /// <summary>U2 EnableUser: the account works again, but every device stays revoked (a new device must be approved).</summary>
    public Result<bool> Enable(Guid actorId, string? reason)
    {
        var t = UserStateMachine.Machine.Resolve(StateName, UserCommands.EnableUser);
        if (!t.IsSuccess) return t.Error!;
        if (actorId == Id) return Error.Sod("You cannot enable your own account.");
        if (string.IsNullOrWhiteSpace(reason)) return Error.Validation("REASON_REQUIRED", "A reason is required.");
        Status = UserStatus.Active;
        DisabledAt = null;
        DisabledBy = null;
        DisableReason = null;
        return true;
    }

    public string StateName => Status == UserStatus.Active ? UserStates.Active : UserStates.Disabled;

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
