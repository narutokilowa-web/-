namespace Mfop.Kernel.Commands;

/// <summary>Who is acting. Roles come from our database (never from token claims).</summary>
public sealed record ActorContext(Guid? UserId, IReadOnlyList<string> Roles, Guid? DeviceId = null)
{
    public const string SystemRole = "SYSTEM";

    public static ActorContext System { get; } = new(null, [SystemRole]);

    public bool IsSystem => UserId is null;

    public bool HasRole(string role) => Roles.Contains(role);
}

/// <summary>Scoped holder of the current actor, set once per request after authentication.</summary>
public sealed class CurrentActor
{
    private ActorContext? _actor;

    public ActorContext? Actor => _actor;

    public ActorContext Required => _actor ?? throw new InvalidOperationException("No authenticated actor in this scope.");

    public void Set(ActorContext actor)
    {
        if (_actor is not null && _actor != actor)
        {
            throw new InvalidOperationException("Actor already set for this scope.");
        }

        _actor = actor;
    }
}
