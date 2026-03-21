using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a user logs out.
/// </summary>
public record UserLoggedOutEvent(
    Guid UserId,
    bool AllDevices) : IDomainEvent;
