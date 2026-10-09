using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user moved their second factor to a new authenticator app: the
/// old app's codes no longer work, and a new set of recovery codes was issued.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, like <see cref="TwoFactorEnabledEvent"/>. Never the
/// secret or the codes.
/// </remarks>
public record TwoFactorAuthenticatorReplacedEvent(
    Guid UserId,
    Guid ReplacedBy,
    string Email,
    string DisplayName,
    string? DeviceName) : IDomainEvent;
