namespace Auth_API.Tests.Helpers;

/// <summary>
/// A clock that reads the system time until a test moves it, and from then on
/// stands still between moves. Lets one test walk a timeline ("signs in at 10:01,
/// comes back at 10:07") through the same handler, while every test that never
/// touches it sees the time it always saw.
/// </summary>
internal sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset? _now;

    public override DateTimeOffset GetUtcNow() => _now ?? base.GetUtcNow();

    /// <summary>The current reading as a UTC <see cref="DateTime"/>.</summary>
    public DateTime UtcNow => GetUtcNow().UtcDateTime;

    /// <summary>Stops the clock at its current reading, then moves it on by <paramref name="by"/>.</summary>
    public void Advance(TimeSpan by) => _now = GetUtcNow() + by;
}
