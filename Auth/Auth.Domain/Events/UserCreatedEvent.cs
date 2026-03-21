using Auth.Domain.Primitives;

namespace Auth.Domain.Events;

/// <summary>
/// Raised when a new user is created.
/// </summary>
public record UserCreatedEvent(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    Guid CreatedBy) : IDomainEvent;
