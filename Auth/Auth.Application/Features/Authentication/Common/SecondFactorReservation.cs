using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Evidence that one second-factor attempt has been counted against the account.
/// <see cref="Interfaces.ISecondFactorVerifier.VerifyAsync"/> accepts nothing else,
/// and only the verifier can create one — so no code reaches a check without its
/// attempt having been reserved first.
/// </summary>
public sealed class SecondFactorReservation
{
    internal SecondFactorReservation(TwoFactorSnapshot snapshot, TwoFactorChallenge? challenge = null)
    {
        Snapshot = snapshot;
        Challenge = challenge;
    }

    /// <summary>
    /// Gets the user's factor as it was read before the attempt was reserved.
    /// </summary>
    public TwoFactorSnapshot Snapshot { get; }

    /// <summary>
    /// Gets the challenge the code completes, as read; null for a reservation that
    /// serves no challenge (switching the factor on or off, a step-up).
    /// </summary>
    public TwoFactorChallenge? Challenge { get; }

    /// <summary>
    /// The same reservation over another view of the factor — the replacement
    /// secret in place of the current one — for the one check that needs it.
    /// </summary>
    internal SecondFactorReservation With(TwoFactorSnapshot snapshot) => new(snapshot, Challenge);
}
