using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Auth_API.Common.RateLimiting;

/// <summary>
/// How many client assertions that did not verify one address may send to the token endpoint
/// in a window (OI-178), sized by the token endpoint's per-address budget.
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
/// A verification in progress holds a place in the allowance until it ends, so a burst of
/// concurrent requests from one address cannot all pass the check before the first of them is
/// counted: at most the allowance's worth of verifications run at once per address.
/// </para>
/// </remarks>
public sealed class TokenAssertionFailureBudget : IDisposable
{
    private readonly PartitionedRateLimiter<string> _failures;
    private readonly ConcurrentDictionary<string, int> _inFlight = new();
    private readonly Func<int> _settingsVersion;

    /// <param name="settingsVersion">The system settings version, stamped into every key.</param>
    /// <param name="perAddress">The allowance per address, read when a window opens.</param>
    public TokenAssertionFailureBudget(Func<int> settingsVersion, Func<FixedWindowBudget> perAddress)
    {
        _settingsVersion = settingsVersion;
        _failures = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => perAddress().Options()));
    }

    /// <summary>
    /// A place for one verification from this address, or null when the address has no room left:
    /// its failures in this window and the verifications still running fill its allowance.
    /// </summary>
    public Reservation? TryReserve(string clientAddress)
    {
        var key = $"v{_settingsVersion()}:{clientAddress}";
        var running = _inFlight.AddOrUpdate(key, 1, (_, count) => count + 1);
        var available = _failures.GetStatistics(key)?.CurrentAvailablePermits ?? long.MaxValue;
        if (available - running >= 0)
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

        /// <summary>The assertion did not verify: one failure is counted on the address.</summary>
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
