using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a new user is created.
/// </summary>
public record UserCreatedEvent(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    Guid CreatedBy) : INotification;
