using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a user successfully logs in.
/// </summary>
public record UserLoggedInEvent(
    Guid UserId,
    string Email,
    string? IpAddress,
    string? UserAgent) : INotification;
