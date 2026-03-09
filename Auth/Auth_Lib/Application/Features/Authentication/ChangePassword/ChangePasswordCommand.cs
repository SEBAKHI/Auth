using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.ChangePassword;

/// <summary>
/// Command to change a user's password.
/// </summary>
/// <param name="UserId">The ID of the user changing their password.</param>
/// <param name="CurrentPassword">The user's current password for verification.</param>
/// <param name="NewPassword">The new password to set.</param>
/// <param name="TerminateSessions">
/// Whether to terminate sessions after password change.
/// Null means use server configuration default.
/// </param>
/// <param name="CurrentSessionId">
/// Optional current session ID to exclude from termination.
/// </param>
public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    bool? TerminateSessions = null,
    Guid? CurrentSessionId = null) : IRequest<ErrorOr<Success>>;
