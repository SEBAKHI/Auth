using System.Collections.Concurrent;

namespace Auth_API.Common.RateLimiting;

/// <summary>
/// Each verified client assertion counts on its application's budget once (OI-178).
/// </summary>
/// <remarks>
/// <para>
/// The token endpoint counts a request on its application's budget before the grant runs, and a
/// grant may refuse before it spends the assertion's jti (an unknown refresh token, a bad code, a
/// scope the application is not allowed). Without this, one valid assertion that someone captured
/// before it was used could be replayed for its whole lifetime, each copy counted on the
/// application's budget and refused by the grant, until the application's own people were refused.
/// So the first sight of an assertion counts on the application; every later copy counts on the
/// address it comes from.
/// </para>
/// <para>
/// Only assertions that verified are recorded, so the set grows with the applications' own traffic,
/// never with an attacker's. An entry is kept until well past its assertion's <c>exp</c> (the
/// verifier accepts some clock skew), then swept. Per process: with several instances, a copy may
/// count once on each.
/// </para>
/// </remarks>
public sealed class TokenAssertionSightings
{
    private const int SweepEvery = 1024;
    private static readonly TimeSpan KeptPastExpiry = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<(Guid ApplicationId, string Jti), DateTime> _seen = new();
    private readonly TimeProvider _clock;
    private int _recorded;

    public TokenAssertionSightings(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>True the first time this application's assertion is seen; false for every copy after it.</summary>
    public bool IsFirst(Guid applicationId, string jti, DateTime expiresAtUtc)
    {
        if (!_seen.TryAdd((applicationId, jti), expiresAtUtc))
        {
            return false;
        }

        if (Interlocked.Increment(ref _recorded) % SweepEvery == 0)
        {
            Sweep();
        }

        return true;
    }

    private void Sweep()
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime - KeptPastExpiry;
        foreach (var (key, expiresAtUtc) in _seen)
        {
            if (expiresAtUtc < cutoff)
            {
                _seen.TryRemove(key, out _);
            }
        }
    }
}
