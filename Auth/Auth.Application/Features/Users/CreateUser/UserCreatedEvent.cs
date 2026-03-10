using MediatR;

namespace Auth.Application.Features.Users.CreateUser;

/// <summary>
/// Published when a new user is created.
/// </summary>
public record UserCreatedEvent(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    Guid CreatedBy) : INotification;
