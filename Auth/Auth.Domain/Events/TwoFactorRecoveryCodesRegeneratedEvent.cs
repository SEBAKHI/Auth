using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user replaced the recovery codes of their second factor with a
/// new set, so the old ones no longer work.
/// </summary>
/// <remarks>
/// Carries the recipient's address and name, and the device the change was made
/// from when it is known, like <see cref="TwoFactorEnabledEvent"/>. Never the codes.
/// </remarks>
public record TwoFactorRecoveryCodesRegeneratedEvent(
    Guid UserId,
    Guid RegeneratedBy,
    string Email,
    string DisplayName,
    string? DeviceName) : IDomainEvent;
