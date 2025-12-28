using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a user's password is changed.
/// </summary>
public record PasswordChangedEvent(
    Guid UserId,
    Guid ChangedBy) : INotification;
