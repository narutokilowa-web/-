namespace Mfop.Kernel;

/// <summary>
/// The platform clock is <see cref="TimeProvider"/> (injectable, fakeable in tests) — our "IClock".
/// PostgreSQL stores timestamps with microsecond precision; audit hashing must use exactly the stored value,
/// so every persisted timestamp is truncated to microseconds.
/// </summary>
public static class ClockExtensions
{
    public static DateTimeOffset UtcNowMicros(this TimeProvider clock) => TruncateToMicros(clock.GetUtcNow());

    public static DateTimeOffset TruncateToMicros(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}
