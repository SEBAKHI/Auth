using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user account is locked.
/// </summary>
public record UserLockedEvent(
    Guid UserId,
    DateTime? LockoutEnd,
    Guid LockedBy) : IDomainEvent;
