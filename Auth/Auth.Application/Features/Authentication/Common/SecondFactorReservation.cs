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
    internal SecondFactorReservation(TwoFactorSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    /// <summary>
    /// Gets the user's factor as it was read before the attempt was reserved.
    /// </summary>
    public TwoFactorSnapshot Snapshot { get; }
}
