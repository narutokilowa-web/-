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
public sealed class User
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
}
