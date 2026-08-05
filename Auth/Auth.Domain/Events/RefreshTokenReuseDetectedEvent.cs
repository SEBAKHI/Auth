using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a refresh token that had already been spent was presented again.
/// The server answers by revoking every token and session the account holds, so
/// by the time this is published the account owner has been signed out of all
/// their devices without having asked to be — they are owed an explanation.
///
/// Deliberately NOT raised for every mass revocation. Signing out of all
/// devices is something the user did on purpose, and account deletion has its
/// own notices; only this path revokes the account's sessions on the strength
/// of a suspicion.
/// </summary>
/// <param name="IpAddress">The address the replayed token arrived from, or null when unavailable.</param>
/// <param name="DetectedAtUtc">When the replay was seen.</param>
public record RefreshTokenReuseDetectedEvent(
    Guid UserId,
    string Email,
    string? DisplayName,
    string? IpAddress,
    DateTime DetectedAtUtc) : IDomainEvent;
