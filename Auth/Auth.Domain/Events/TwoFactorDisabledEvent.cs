using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when two-factor authentication is disabled for a user.
/// </summary>
public record TwoFactorDisabledEvent(
    Guid UserId,
    Guid DisabledBy) : IDomainEvent;
