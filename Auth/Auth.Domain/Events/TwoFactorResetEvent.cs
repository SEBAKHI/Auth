using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when an administrator removed another account's second factor — the
/// way back for an account whose authenticator and recovery codes are both lost.
/// Its sessions and tokens were revoked with it, and its next sign-in sets a
/// factor up again.
/// </summary>
/// <remarks>
/// <see cref="UserId"/> is the account that lost its factor; <see cref="ResetBy"/>
/// the administrator who removed it, which the audit row names and the notice to
/// the account owner does not need. No device: the change was not made from one
/// of the owner's.
/// </remarks>
public record TwoFactorResetEvent(
    Guid UserId,
    Guid ResetBy,
    string Email,
    string DisplayName) : IDomainEvent;
