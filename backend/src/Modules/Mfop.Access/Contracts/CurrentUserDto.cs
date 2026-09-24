namespace Mfop.Access.Contracts;

/// <param name="RolesSource">Always "database": roles are read from our DB, never from token claims (ADR-0003).</param>
public sealed record CurrentUserDto(Guid UserId, string Username, string DisplayName, IReadOnlyList<string> Roles, string RolesSource);

public enum CurrentUserOutcome
{
    Ok,
    NotProvisioned,
    Disabled,
}

public sealed record CurrentUserResult(CurrentUserOutcome Outcome, CurrentUserDto? User)
{
    public static CurrentUserResult Ok(CurrentUserDto user) => new(CurrentUserOutcome.Ok, user);
    public static CurrentUserResult NotProvisioned() => new(CurrentUserOutcome.NotProvisioned, null);
    public static CurrentUserResult Disabled() => new(CurrentUserOutcome.Disabled, null);
}
