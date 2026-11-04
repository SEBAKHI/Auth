namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Whether this process has loaded the durable token revocation list (X04, RV2 (e)).
/// Written by <see cref="TokenRevocationBackgroundService"/>, read by the readiness
/// check and the security posture.
/// </summary>
/// <remarks>
/// Loaded stays loaded: a later periodic re-read that fails leaves the list in memory as
/// it was, so it does not turn the process unready.
/// </remarks>
public sealed class RevocationRehydrationStatus
{
    private int _loaded;
    private int _failedAttempts;

    /// <summary>Whether a load has succeeded since the process started.</summary>
    public bool IsLoaded => Volatile.Read(ref _loaded) == 1;

    /// <summary>How many loads failed before the first one succeeded (or so far, while none has).</summary>
    public int FailedAttempts => Volatile.Read(ref _failedAttempts);

    internal void RecordLoaded() => Volatile.Write(ref _loaded, 1);

    internal void RecordFailed()
    {
        if (!IsLoaded)
        {
            Interlocked.Increment(ref _failedAttempts);
        }
    }
}
