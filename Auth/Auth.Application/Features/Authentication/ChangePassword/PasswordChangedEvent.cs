using MediatR;

namespace Auth.Application.Features.Authentication.ChangePassword;

/// <summary>
/// Published when a user's password is changed.
/// </summary>
public record PasswordChangedEvent(
    Guid UserId,
    Guid ChangedBy) : INotification;
