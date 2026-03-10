using MediatR;

namespace Auth.Application.Features.Authentication.Login;

/// <summary>
/// Published when a user successfully logs in.
/// </summary>
public record UserLoggedInEvent(
    Guid UserId,
    string Email,
    string? IpAddress,
    string? UserAgent) : INotification;
