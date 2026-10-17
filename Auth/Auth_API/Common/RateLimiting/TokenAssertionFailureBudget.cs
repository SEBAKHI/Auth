using System.Threading.RateLimiting;

namespace Auth_API.Common.RateLimiting;

/// <summary>
/// How many client assertions that did not verify one address may send to the token endpoint
/// in a window (OI-178), sized by the token endpoint's per-address budget.
/// </summary>
/// <remarks>
/// Verification happens before the rate limiter, so that a verified application can count on
/// its own budget, and it reads the database. Once an address has spent this allowance, its
/// further assertions are refused without being verified until the window ends. A server that
/// shares its egress address with someone sending bad assertions shares this allowance too:
/// the same exposure the per-address budget had before.
/// </remarks>
public sealed class TokenAssertionFailureBudget : IDisposable
{
    private readonly PartitionedRateLimiter<string> _failures;
    private readonly Func<int> _settingsVersion;

    /// <param name="settingsVersion">The system settings version, stamped into every key.</param>
    /// <param name="perAddress">The allowance per address, read when a window opens.</param>
    public TokenAssertionFailureBudget(Func<int> settingsVersion, Func<FixedWindowBudget> perAddress)
    {
        _settingsVersion = settingsVersion;
        _failures = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => perAddress().Options()));
    }

    /// <summary>Whether this address may still have an assertion verified in this window.</summary>
    public bool HasRoom(string clientAddress) =>
        _failures.GetStatistics(Key(clientAddress)) is not { CurrentAvailablePermits: <= 0 };

    /// <summary>Counts one assertion from this address that did not verify.</summary>
    public void Spend(string clientAddress)
    {
        using var lease = _failures.AttemptAcquire(Key(clientAddress));
    }

    private string Key(string clientAddress) => $"v{_settingsVersion()}:{clientAddress}";

    public void Dispose() => _failures.Dispose();
}
