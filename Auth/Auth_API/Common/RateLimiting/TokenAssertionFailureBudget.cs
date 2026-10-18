using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Auth_API.Common.RateLimiting;

/// <summary>
/// How many client assertions that did not reach an application's budget one address may send to
/// the token endpoint in a window (OI-178): assertions that did not verify, copies of one already
/// counted, and verifications that threw or were abandoned. Sized by the token endpoint's
/// per-address budget.
/// </summary>
/// <remarks>
/// <para>
/// Verification happens before the rate limiter, so that a verified application can count on
/// its own budget, and it reads the database. Once an address has spent this allowance, its
/// further assertions are refused without being verified until the window ends. A server that
/// shares its egress address with someone sending bad assertions shares this allowance too:
/// the same exposure the per-address budget had before.
/// </para>
/// <para>
/// Verifications in progress are held back only once the address has had a failure in the
/// window. Until then nothing it sent has failed, and a server renewing many people at the same
/// instant is never capped by concurrency: its requests reach its application's budget. From the
/// first failure on, each running verification holds a place in the allowance until it ends, so
/// a burst cannot pass the check before its failures are counted. What an address sending junk
/// gets is therefore one wave per window: the requests that arrive while its first failure is
/// still being verified, bounded by the edge's ceiling for requests that carry an assertion.
/// </para>
/// </remarks>
public sealed class TokenAssertionFailureBudget : IDisposable
{
    private readonly PartitionedRateLimiter<string> _failures;
    private readonly ConcurrentDictionary<string, int> _inFlight = new();
    private readonly Func<int> _settingsVersion;
    private readonly Func<FixedWindowBudget> _perAddress;

    /// <param name="settingsVersion">The system settings version, stamped into every key.</param>
    /// <param name="perAddress">The allowance per address, read when a window opens.</param>
    public TokenAssertionFailureBudget(Func<int> settingsVersion, Func<FixedWindowBudget> perAddress)
    {
        _settingsVersion = settingsVersion;
        _perAddress = perAddress;
        _failures = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => perAddress().Options()));
    }

    /// <summary>
    /// A place for one verification from this address, or null when the address has no room left:
    /// it has failed in this window, and its failures and the verifications still running fill its
    /// allowance.
    /// </summary>
    public Reservation? TryReserve(string clientAddress)
    {
        var key = $"v{_settingsVersion()}:{clientAddress}";
        var running = _inFlight.AddOrUpdate(key, 1, (_, count) => count + 1);
        var available = _failures.GetStatistics(key)?.CurrentAvailablePermits ?? long.MaxValue;

        // The allowance is read again here; the key carries the settings version, so a saved
        // change opens a fresh partition sized by the same reading.
        var failedThisWindow = available < _perAddress().PermitLimit;
        if (!failedThisWindow || available - running >= 0)
        {
            return new Reservation(this, key);
        }

        Release(key);
        return null;
    }

    private void Release(string key)
    {
        if (_inFlight.AddOrUpdate(key, 0, (_, count) => count - 1) <= 0)
        {
            _inFlight.TryRemove(new KeyValuePair<string, int>(key, 0));
        }
    }

    public void Dispose() => _failures.Dispose();

    /// <summary>One verification's place in the allowance, held until disposed.</summary>
    public sealed class Reservation : IDisposable
    {
        private readonly TokenAssertionFailureBudget _budget;
        private readonly string _key;
        private bool _released;

        internal Reservation(TokenAssertionFailureBudget budget, string key)
        {
            _budget = budget;
            _key = key;
        }

        /// <summary>
        /// The request did not reach an application's budget (its assertion did not verify, was a
        /// copy of one already counted, or its verification failed): one failure is counted on the
        /// address.
        /// </summary>
        public void Failed()
        {
            using var lease = _budget._failures.AttemptAcquire(_key);
        }

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                _budget.Release(_key);
            }
        }
    }
}
