using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when two-factor authentication is enabled for a user.
/// </summary>
public record TwoFactorEnabledEvent(
    Guid UserId,
    Guid EnabledBy) : IDomainEvent;
