using Auth.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The one rule deciding whether an account binding its FIRST second factor must
/// also enter a code emailed to its confirmed address.
/// </summary>
/// <remarks>
/// Whoever holds only the password could otherwise bind an authenticator of their
/// own to an account that has none, keep its secret and recovery codes, and lock
/// the owner out — a password reset does not remove a factor. Proving the mailbox
/// closes that for every account whose mailbox the attacker does not also hold.
/// <para>
/// The code is required only while email is on: with <c>Email:Enabled</c> false
/// nothing could deliver it, and the factor binds with the notice alone, as before —
/// so the rule never becomes a dead end. <c>TwoFactor:RequireEmailCodeForFirstFactor</c>
/// is the rollout switch in front of it.
/// </para>
/// </remarks>
public class FirstFactorEmailProofPolicy
{
    private readonly IOptionsMonitor<TwoFactorSettings> _twoFactor;
    private readonly IOptionsMonitor<EmailSettings> _email;

    public FirstFactorEmailProofPolicy(
        IOptionsMonitor<TwoFactorSettings> twoFactor,
        IOptionsMonitor<EmailSettings> email)
    {
        _twoFactor = twoFactor;
        _email = email;
    }

    /// <summary>
    /// Gets whether binding a first factor needs the emailed code right now.
    /// </summary>
    /// <remarks>
    /// Read per call, so both settings are hot. A request reads it once and keeps
    /// the answer, so a setting flipped mid-request cannot split one decision in two.
    /// <c>Email:Enabled</c> is read the way the email channel reads it, so the code is
    /// never required at an instant the channel would not send it.
    /// </remarks>
    public bool IsRequired =>
        _twoFactor.CurrentValue.RequireEmailCodeForFirstFactor && _email.CurrentValue.Enabled;
}
