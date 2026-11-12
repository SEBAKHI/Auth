using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Common.BotDefense;

/// <summary>
/// What an enforced check does with a request it could not judge (S13, BD6): Cloudflare
/// unreachable, or the server's keys not in place.
/// </summary>
/// <remarks>
/// <para>
/// <c>PerSurface</c> (the default): a sign-up start is refused; a password sign-in, a code start
/// and a reset request take a permit from the per-address allowance and go on, and are refused
/// once it is spent. An outage would otherwise lock every account holder out, administrators
/// included, while the allowance keeps a robot to a few attempts per address.
/// </para>
/// <para><c>Closed</c>: every protected request is refused until the check runs again.</para>
/// </remarks>
public sealed class BotDefenseFailurePolicy
{
    private readonly IBotFallbackLimiter _allowance;

    public BotDefenseFailurePolicy(IBotFallbackLimiter allowance)
    {
        _allowance = allowance;
    }

    /// <summary>Success when the request goes on; <c>BotDefense.Unavailable</c> otherwise.</summary>
    /// <param name="failMode">The fail mode in force.</param>
    /// <param name="action">The surface's action.</param>
    /// <param name="clientIp">The caller's address, the allowance's key.</param>
    public ErrorOr<Success> Decide(BotDefenseFailMode failMode, string action, string? clientIp)
    {
        if (failMode == BotDefenseFailMode.Closed || !BotDefenseActions.ThroughAllowanceWhenUnavailable.Contains(action))
        {
            return BotDefenseErrors.Unavailable;
        }

        return _allowance.TryAcquire(clientIp) ? Result.Success : BotDefenseErrors.Unavailable;
    }
}
