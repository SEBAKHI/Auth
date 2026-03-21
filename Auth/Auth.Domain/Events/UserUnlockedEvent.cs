using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user account is unlocked.
/// </summary>
public record UserUnlockedEvent(
    Guid UserId,
    Guid UnlockedBy) : IDomainEvent;
