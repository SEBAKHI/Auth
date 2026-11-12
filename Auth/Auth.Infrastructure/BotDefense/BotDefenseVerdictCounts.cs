using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;

namespace Auth.Infrastructure.BotDefense;

/// <summary>
/// The verdicts this process has seen, for the posture (S13, BD12): the pass rate per app since
/// the process started, and the share of submissions without a token in the last ten minutes
/// under Enforce. Per process (D5) and lost with it; the owner's gate is read from the log by
/// <c>Tools/probes/verdict-window.mjs</c>, which sees every process and every day.
/// </summary>
public sealed class BotDefenseVerdictCounts
{
    /// <summary>The window the token-missing share is read over.</summary>
    public static readonly TimeSpan SpikeWindow = TimeSpan.FromMinutes(10);

    /// <summary>Fewer submissions than this in the window say nothing (NODATA).</summary>
    public const int SpikeMinimumSubmissions = 20;

    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long Submissions, long Passed)> _byOrigin = new(StringComparer.Ordinal);
    private readonly Queue<(DateTimeOffset At, bool Missing)> _enforced = new();

    public BotDefenseVerdictCounts(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>Counts one verdict. An Unconfigured one is no check and is not counted.</summary>
    public void Add(string origin, BotDefenseMode mode, BotDefenseVerdictKind kind)
    {
        if (kind == BotDefenseVerdictKind.Unconfigured)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            var (submissions, passed) = _byOrigin.GetValueOrDefault(origin);
            _byOrigin[origin] = (submissions + 1, passed + (kind == BotDefenseVerdictKind.Passed ? 1 : 0));

            if (mode == BotDefenseMode.Enforce)
            {
                _enforced.Enqueue((now, kind == BotDefenseVerdictKind.Missing));
                Prune(now);
            }
        }
    }

    /// <summary>Submissions and passes per app since the process started.</summary>
    public IReadOnlyDictionary<string, (long Submissions, long Passed)> PassRates()
    {
        lock (_gate)
        {
            return new Dictionary<string, (long Submissions, long Passed)>(_byOrigin, StringComparer.Ordinal);
        }
    }

    /// <summary>Submissions under Enforce in the last ten minutes, and how many carried no token.</summary>
    public (int Submissions, int Missing) EnforcedWindow()
    {
        lock (_gate)
        {
            Prune(_timeProvider.GetUtcNow());
            return (_enforced.Count, _enforced.Count(entry => entry.Missing));
        }
    }

    private void Prune(DateTimeOffset now)
    {
        while (_enforced.Count > 0 && now - _enforced.Peek().At > SpikeWindow)
        {
            _enforced.Dequeue();
        }
    }
}
