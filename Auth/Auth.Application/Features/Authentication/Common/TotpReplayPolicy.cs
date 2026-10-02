using Auth.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The one rule deciding whether an authenticator-app code may be accepted a
/// second time, shared by every check that claims a code's time step: sign-in,
/// account recovery, and switching two-factor on or off.
/// </summary>
/// <remarks>
/// A TOTP code stays valid for about 90 seconds (its own step and one either
/// side). Someone who holds the password and sees the code a user just typed —
/// a phishing page relaying it, a shoulder, a shared screen — could otherwise use
/// it again inside that window. The store refuses the reuse; this only says
/// whether it should, so the rule lives in one place and the store has none.
/// </remarks>
public class TotpReplayPolicy
{
    private readonly IOptionsMonitor<TwoFactorSettings> _settings;

    public TotpReplayPolicy(IOptionsMonitor<TwoFactorSettings> settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Gets whether a code whose time step was already accepted is refused.
    /// </summary>
    /// <remarks>
    /// Read per call, so the rollout switch can be turned off during an incident —
    /// or straight back on — without a restart.
    /// </remarks>
    public bool RejectReusedCodes => _settings.CurrentValue.RejectReusedCodes;
}
