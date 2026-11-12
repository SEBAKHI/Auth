using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Common.BotDefense;

/// <summary>
/// What an enforced check does with a request it could not judge (S13, BD6): a token the server
/// could not get Cloudflare's answer about, or the server's keys not in place.
/// </summary>
/// <remarks>
/// <para>
/// <c>PerSurface</c> (the default): <c>registration/start</c> is refused; a password sign-in, a
/// code start and a reset request take a permit from the per-address allowance and go on, and
/// are refused once it is spent. A code start can also begin a new account by code while
/// sign-up is open, so sign-ups by code continue at the allowance's rate. An outage of
/// siteverify alone would otherwise lock every account holder out, administrators included,
/// while the allowance keeps a robot to a few attempts per address.
/// </para>
/// <para><c>Closed</c>: every protected request is refused until the check runs again.</para>
/// <para>
/// Neither covers a browser that got no token at all (Cloudflare's widget blocked or down, a
/// wrong sitekey, an app host missing from the widget): its request is <c>Missing</c>, refused
/// under Enforce whatever the fail mode, and counted by the posture's
/// <c>bot.token-missing-spike</c>. The way out then is the owner's break-glass.
/// </para>
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
