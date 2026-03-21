using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user's password is changed.
/// </summary>
public record PasswordChangedEvent(
    Guid UserId,
    Guid ChangedBy) : IDomainEvent;
