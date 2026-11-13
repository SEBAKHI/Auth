using Auth.Infrastructure.Security;

namespace Auth.Infrastructure.BotDefense;

/// <summary>
/// Whether Cloudflare looks unreachable from this process (S13, OD2): the siteverify breaker
/// tripped, or the posture's own reachability probe got no answer in the last five minutes.
/// </summary>
/// <remarks>
/// <para>
/// In a full Cloudflare outage the widget does not load either, so browsers send no token and
/// no verification is attempted: the breaker alone would never trip. The probe (the public dummy
/// token with the secret, made by <c>BotDefensePostureContributor</c> at most once in five
/// minutes, and evaluated every minute) sees the outage without traffic. While either says so, a
/// request without a token is <c>Unavailable</c>, not <c>Missing</c>, so the fail mode applies:
/// under <c>PerSurface</c> the sign-in, the code start and the reset request go on through the
/// per-address allowance. Robots gain nothing new: during such an outage any printable token
/// already reaches the allowance.
/// </para>
/// <para>A probe that got an answer (a refused secret included) clears the probe's half at once.</para>
/// </remarks>
public sealed class BotDefenseOutageSignal
{
    /// <summary>
    /// How long a failed probe counts: the probe's cache, plus two evaluations (REVIEW-FIXES 167
    /// F13). The next probe starts at the first tick after the cache lapses and records its result
    /// only when it ends, up to a deadline later; equal to the cache, the window left those seconds
    /// uncovered every cycle. An answered probe still clears it at once, so recovery is not slowed.
    /// </summary>
    public static readonly TimeSpan ProbeWindow =
        BotDefensePostureContributor.ProbeCacheDuration + BotDefensePostureContributor.Cadence * 2;

    private readonly BotDefenseCircuitBreaker _breaker;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private DateTimeOffset? _probeFailedAt;

    public BotDefenseOutageSignal(BotDefenseCircuitBreaker breaker, TimeProvider timeProvider)
    {
        _breaker = breaker;
        _timeProvider = timeProvider;
    }

    /// <summary>The reachability probe got Cloudflare's answer (<paramref name="answered"/>), or none.</summary>
    public void RecordProbe(bool answered)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            _probeFailedAt = answered ? null : now;
        }
    }

    /// <summary>Whether a request without a token is to be read as an outage rather than as a missing token.</summary>
    public bool CloudflareUnreachable()
    {
        if (_breaker.Tripped)
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            return _probeFailedAt is { } failedAt && now - failedAt < ProbeWindow;
        }
    }
}
