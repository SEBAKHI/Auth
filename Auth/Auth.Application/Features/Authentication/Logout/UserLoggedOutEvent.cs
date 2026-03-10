using MediatR;

namespace Auth.Application.Features.Authentication.Logout;

/// <summary>
/// Published when a user logs out.
/// </summary>
public record UserLoggedOutEvent(
    Guid UserId,
    bool AllDevices) : INotification;
