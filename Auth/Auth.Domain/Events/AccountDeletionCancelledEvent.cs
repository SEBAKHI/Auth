using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user recovers their account during the grace window,
/// cancelling the pending deletion.
/// </summary>
public record AccountDeletionCancelledEvent(
    Guid UserId,
    string Email,
    string DisplayName,
    DateTime CancelledAtUtc) : IDomainEvent;
