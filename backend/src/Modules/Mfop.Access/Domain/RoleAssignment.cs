using Mfop.Access.Contracts;
using Mfop.Kernel.Auditing;

namespace Mfop.Access.Domain;

/// <summary>
/// Grant of a role to a user. Revocation sets <see cref="RevokedAt"/>; rows are never deleted.
/// </summary>
public sealed class RoleAssignment : IAuditable
{
    private RoleAssignment() { }

    public RoleAssignment(Guid id, Guid userId, string role, Guid? grantedBy, string grantReason, DateTimeOffset grantedAt)
    {
        if (!RoleCodes.All.Contains(role))
        {
            throw new ArgumentException($"Unknown role code '{role}'.", nameof(role));
        }

        Id = id;
        UserId = userId;
        Role = role;
        GrantedBy = grantedBy;
        GrantReason = grantReason;
        GrantedAt = grantedAt;
    }

    public string AuditEntityType => "role_assignment";

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public Guid? GrantedBy { get; private set; }
    public string GrantReason { get; private set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public string? RevokeReason { get; private set; }

    public bool IsActive(DateTimeOffset now) => GrantedAt <= now && (RevokedAt is null || RevokedAt > now);
}
