using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when two-factor authentication is enabled for a user.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, so the notice that tells the owner does not have to load
/// the user again — the pattern of <see cref="PasswordChangedEvent"/>.
/// <see cref="ReplacedEmailCode"/> marks an authenticator app that took the place of
/// the emailed code (S24): the owner's notice names that change, not a plain switch-on.
/// </remarks>
public record TwoFactorEnabledEvent(
    Guid UserId,
    Guid EnabledBy,
    string Email,
    string DisplayName,
    string? DeviceName,
    bool ReplacedEmailCode = false) : IDomainEvent;
