using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;

namespace Auth.Infrastructure.BotDefense;

/// <summary>
/// The verdicts this process has seen, for the posture (S13, BD12): the pass rate per app since
/// the process started, and, over the last ten minutes under Enforce, the share of submissions
/// without a token and the share refused for the deployment's own setup. Per process (D5) and
/// lost with it; the owner's gate is read from the log by <c>Tools/probes/verdict-window.mjs</c>,
/// which sees every process and every day and counts the same way.
/// </summary>
/// <remarks>
/// The window is ten one-minute buckets, so its size does not grow with the request rate: a
/// flood costs a few additions under the lock, never a longer queue to walk.
/// </remarks>
public sealed class BotDefenseVerdictCounts
{
    /// <summary>The window the token-missing share is read over.</summary>
    public static readonly TimeSpan SpikeWindow = TimeSpan.FromMinutes(10);

    /// <summary>Fewer submissions than this in the window say nothing (NODATA).</summary>
    public const int SpikeMinimumSubmissions = 20;

    private const int BucketCount = 10;

    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long Submissions, long Passed, long Junk)> _byOrigin = new(StringComparer.Ordinal);
    private readonly (long Minute, int Submissions, int Missing, int LocalRejected)[] _enforced =
        new (long, int, int, int)[BucketCount];

    public BotDefenseVerdictCounts(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Counts one verdict. An Unconfigured one is no check and is not counted.
    /// <paramref name="localRejection"/>: refused for the deployment's own setup (a host, an
    /// action, a clock), not for the visitor. <paramref name="junkToken"/>: a token no real
    /// widget makes (<see cref="TurnstileBotDefenseVerifier.JunkTokenCodes"/>), counted apart and
    /// never as a submission.
    /// </summary>
    public void Add(
        string origin, BotDefenseMode mode, BotDefenseVerdictKind kind, bool localRejection = false, bool junkToken = false)
    {
        if (kind == BotDefenseVerdictKind.Unconfigured)
        {
            return;
        }

        var minute = Minute(_timeProvider.GetUtcNow());
        lock (_gate)
        {
            var (submissions, passed, junk) = _byOrigin.GetValueOrDefault(origin);
            if (junkToken)
            {
                _byOrigin[origin] = (submissions, passed, junk + 1);
                return;
            }

            _byOrigin[origin] = (submissions + 1, passed + (kind == BotDefenseVerdictKind.Passed ? 1 : 0), junk);

            if (mode == BotDefenseMode.Enforce)
            {
                var index = (int)(minute % BucketCount);
                var bucket = _enforced[index];
                if (bucket.Minute != minute)
                {
                    bucket = (minute, 0, 0, 0);
                }

                _enforced[index] = (
                    minute,
                    bucket.Submissions + 1,
                    bucket.Missing + (kind == BotDefenseVerdictKind.Missing ? 1 : 0),
                    bucket.LocalRejected + (localRejection ? 1 : 0));
            }
        }
    }

    /// <summary>Submissions, passes and junk tokens per app since the process started.</summary>
    public IReadOnlyDictionary<string, (long Submissions, long Passed, long Junk)> PassRates()
    {
        lock (_gate)
        {
            return new Dictionary<string, (long Submissions, long Passed, long Junk)>(_byOrigin, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Submissions under Enforce in the last ten minutes (the current minute and the nine before
    /// it), how many carried no token, and how many were refused for the deployment's own setup.
    /// </summary>
    public (int Submissions, int Missing, int LocalRejected) EnforcedWindow()
    {
        var oldest = Minute(_timeProvider.GetUtcNow()) - (BucketCount - 1);
        lock (_gate)
        {
            var (submissions, missing, localRejected) = (0, 0, 0);
            foreach (var bucket in _enforced)
            {
                if (bucket.Submissions > 0 && bucket.Minute >= oldest)
                {
                    submissions += bucket.Submissions;
                    missing += bucket.Missing;
                    localRejected += bucket.LocalRejected;
                }
            }

            return (submissions, missing, localRejected);
        }
    }

    private static long Minute(DateTimeOffset at) => at.ToUnixTimeSeconds() / 60;
}
