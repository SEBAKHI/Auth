using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a user logs out.
/// </summary>
public record UserLoggedOutEvent(
    Guid UserId,
    bool AllDevices) : INotification;
