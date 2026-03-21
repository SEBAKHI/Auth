using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user account is deleted.
/// </summary>
public record UserDeletedEvent(
    Guid UserId,
    string Email,
    Guid DeletedBy) : IDomainEvent;
