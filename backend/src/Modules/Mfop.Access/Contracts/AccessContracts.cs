namespace Mfop.Access.Contracts;

/// <param name="RolesSource">Always "database": roles are read from our DB, never from token claims (ADR-0003).</param>
public sealed record CurrentUserDto(Guid UserId, string Username, string DisplayName, IReadOnlyList<string> Roles, string RolesSource);

/// <summary>Display names for audit timelines and lists.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> DisplayNamesAsync(IEnumerable<Guid> userIds, CancellationToken ct);

    /// <summary>The users among <paramref name="userIds"/> that are not ACTIVE (disabled / offboarded).</summary>
    Task<IReadOnlySet<Guid>> InactiveAsync(IEnumerable<Guid> userIds, CancellationToken ct);
}
