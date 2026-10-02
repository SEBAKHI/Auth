using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when two-factor authentication is enabled for a user.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, so the notice that tells the owner does not have to load
/// the user again — the pattern of <see cref="PasswordChangedEvent"/>.
/// </remarks>
public record TwoFactorEnabledEvent(
    Guid UserId,
    Guid EnabledBy,
    string Email,
    string DisplayName,
    string? DeviceName) : IDomainEvent;
