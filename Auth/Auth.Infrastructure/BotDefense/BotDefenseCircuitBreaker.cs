using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.BotDefense;

/// <summary>
/// The siteverify circuit breaker (S13, BD4): after five consecutive verifications that could not
/// reach an answer, no call is made for 30 seconds, so an outage costs a visitor no deadline
/// wait. One per process (D5): a second API process keeps its own.
/// </summary>
/// <remarks>
/// After the open period one call goes through as a probe: an answer closes the breaker, another
/// failure opens it again. A refused token is an answer, so junk tokens alone do not open it;
/// volume can: a 429 is a failure, and a flood of tokens that makes Cloudflare throttle this
/// server opens it, after which the fail mode applies until an answer closes it (REVIEW 167
/// F5). The 429s of the last ten minutes are counted for the posture's
/// <c>bot.siteverify-throttled</c>. State changes are logged (opened with its cause, closed);
/// each call is not.
/// </remarks>
public sealed class BotDefenseCircuitBreaker
{
    /// <summary>Consecutive failures that open the breaker.</summary>
    public const int FailureThreshold = 5;

    /// <summary>The failure a 429 answer is recorded as.</summary>
    public const string ThrottledFailure = "http-429";

    private const int ThrottleBuckets = 10;

    /// <summary>How long the breaker stays open.</summary>
    public static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long one probe may hold the half-open breaker. Longer than any deadline, so a probe
    /// that never reported back (its caller went away) cannot keep the breaker shut for good.
    /// </summary>
    public static readonly TimeSpan ProbeLease = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BotDefenseCircuitBreaker> _logger;
    private readonly Lock _gate = new();
    private readonly (long Minute, int Count)[] _throttled = new (long, int)[ThrottleBuckets];
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _probing;
    private DateTimeOffset _probeStartedAt;

    public BotDefenseCircuitBreaker(TimeProvider timeProvider, ILogger<BotDefenseCircuitBreaker> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Whether a call may be made now. While open, false; after it, one probe at a time.</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_openUntil is not { } until)
            {
                return true;
            }

            var now = _timeProvider.GetUtcNow();
            if (now < until || (_probing && now - _probeStartedAt < ProbeLease))
            {
                return false;
            }

            _probing = true;
            _probeStartedAt = now;
            return true;
        }
    }

    /// <summary>A call reached an answer: the count restarts, and an open breaker closes.</summary>
    public void RecordAnswer()
    {
        bool closed;
        lock (_gate)
        {
            closed = _openUntil is not null;
            _consecutiveFailures = 0;
            _openUntil = null;
            _probing = false;
        }

        if (closed)
        {
            _logger.LogInformation("Bot defense: siteverify answered again; the circuit breaker is closed");
        }
    }

    /// <summary>A call could not reach an answer (<paramref name="cause"/>, for the log line).</summary>
    public void RecordFailure(string cause)
    {
        bool opened;
        int failures;
        lock (_gate)
        {
            if (cause == ThrottledFailure)
            {
                var minute = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
                var index = (int)(minute % ThrottleBuckets);
                var bucket = _throttled[index];
                _throttled[index] = (minute, bucket.Minute == minute ? bucket.Count + 1 : 1);
            }

            _consecutiveFailures++;
            failures = _consecutiveFailures;
            // Closed and at the threshold, or the probe after an open period failed.
            opened = _probing || (_openUntil is null && _consecutiveFailures >= FailureThreshold);
            if (opened)
            {
                _openUntil = _timeProvider.GetUtcNow() + OpenDuration;
            }

            _probing = false;
        }

        if (opened)
        {
            _logger.LogWarning(
                "Bot defense: siteverify failed {Failures} times in a row (last: {Cause}); the circuit breaker is open for {Seconds} s, during which no call is made",
                failures, cause, (int)OpenDuration.TotalSeconds);
        }
    }

    /// <summary>
    /// A call ended without an outcome (its caller went away): neither an answer nor a failure.
    /// A probe it held is given back, so the next call may probe.
    /// </summary>
    public void Abandon()
    {
        lock (_gate)
        {
            _probing = false;
        }
    }

    /// <summary>
    /// Whether the breaker opened and no answer has closed it since: open now, or waiting for the
    /// call that would close it. Cloudflare has not answered this process since its last run of
    /// failures (S13, OD2).
    /// </summary>
    public bool Tripped
    {
        get
        {
            lock (_gate)
            {
                return _openUntil is not null;
            }
        }
    }

    /// <summary>How many 429 answers Cloudflare gave in the last ten minutes (the current minute and the nine before it).</summary>
    public int ThrottledInWindow()
    {
        var oldest = _timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60 - (ThrottleBuckets - 1);
        lock (_gate)
        {
            return _throttled.Where(bucket => bucket.Count > 0 && bucket.Minute >= oldest).Sum(bucket => bucket.Count);
        }
    }

    /// <summary>Whether the breaker is open now (for the posture).</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _openUntil is { } until && _timeProvider.GetUtcNow() < until;
            }
        }
    }
}
