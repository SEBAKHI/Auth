using System.Threading.RateLimiting;
using Auth.Application.Interfaces;

namespace Auth_API.Common.RateLimiting;

/// <summary>
/// The <c>bot-fallback</c> allowance (S13, BD6, RL1): while the bot check cannot run, a password
/// sign-in, a code start or a reset request from one address goes on this many times per window,
/// <c>RateLimiting:BotFallbackPermitLimit</c> per <c>RateLimiting:BotFallbackWindowSeconds</c>
/// (5 per 300 seconds by default), and is refused after.
/// </summary>
/// <remarks>
/// A policy with no endpoint: the bot-check behavior takes a permit itself. Keyed like the named
/// policies, <c>v{settings version}:{address}</c> with <c>unknown</c> for a request without
/// one, so a saved limit opens fresh windows; read when a window opens. Per process, in memory
/// (RL8): rate shaping in front of the account lock, not a boundary of its own.
/// </remarks>
public sealed class BotFallbackLimiter : IBotFallbackLimiter, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly Func<int> _settingsVersion;

    /// <param name="settingsVersion">The system settings version, stamped into every key.</param>
    /// <param name="allowance">The allowance per address, read when a window opens.</param>
    public BotFallbackLimiter(Func<int> settingsVersion, Func<FixedWindowBudget> allowance)
    {
        _settingsVersion = settingsVersion;
        _limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => allowance().Options()));
    }

    /// <summary>The partition key for <paramref name="clientIp"/>.</summary>
    public string KeyFor(string? clientIp) => $"v{_settingsVersion()}:{clientIp ?? "unknown"}";

    /// <inheritdoc />
    public bool TryAcquire(string? clientIp)
    {
        using var lease = _limiter.AttemptAcquire(KeyFor(clientIp));
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
