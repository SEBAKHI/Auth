namespace Auth.Infrastructure.AccountDeletion;

/// <summary>
/// When the retention sweep last finished in this process. Written by
/// <see cref="AccountDeletionWorker"/>, read by the readiness check, so the
/// question "is the sweep still running?" is answered without a heartbeat row in
/// the audit log.
/// </summary>
/// <remarks>
/// Held in memory on purpose. The worker sweeps on every start, so a fresh process
/// knows the answer after its first cycle, and no table or deployment step is
/// needed. Before the first success the gap is measured from the process start, so
/// a sweep that fails from the very first run still turns overdue.
/// </remarks>
public sealed class RetentionSweepStatus
{
    private readonly DateTime _startedAtUtc;

    // DateTime.Ticks of the last success; 0 until the first one.
    private long _lastSuccessTicks;

    public RetentionSweepStatus(TimeProvider timeProvider)
    {
        _startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
    }

    /// <summary>When the last sweep of this process finished, or null before the first.</summary>
    public DateTime? LastSuccessUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// The longest gap a working sweep leaves between two successes: one UTC day,
    /// plus one poll (the daily sweep runs on the first poll after midnight), plus
    /// an hour for the sweep itself and a slow cycle.
    /// </summary>
    public static TimeSpan MaxHealthyGap(TimeSpan pollInterval) =>
        TimeSpan.FromDays(1) + pollInterval + TimeSpan.FromHours(1);

    /// <summary>
    /// Whether more than <see cref="MaxHealthyGap"/> has passed since the last
    /// success, or since the process started when none has succeeded yet.
    /// </summary>
    public bool IsOverdue(DateTime nowUtc, TimeSpan pollInterval) =>
        nowUtc - (LastSuccessUtc ?? _startedAtUtc) > MaxHealthyGap(pollInterval);

    internal void RecordSuccess(DateTime finishedAtUtc) =>
        Interlocked.Exchange(ref _lastSuccessTicks, finishedAtUtc.Ticks);
}
