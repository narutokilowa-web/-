namespace Mfop.Kernel;

/// <summary>Identifiers are UUIDv7 (time-ordered) so devices can create them offline without collisions.</summary>
public static class Ids
{
    public static Guid New() => Guid.CreateVersion7();
}
