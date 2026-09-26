using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ChangePassword;

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
/// <param name="IdpSessionToken">
/// The caller's SSO cookie value, if the browser presented one, so the sweep
/// ends every OTHER browser's SSO session without ending the one being used to
/// change the password.
/// </param>
/// <param name="ConfirmNewPassword">
/// The new password typed a second time, which must equal <paramref name="NewPassword"/>.
/// Null when the caller carries no confirmation.
/// </param>
public record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword,
    bool? TerminateSessions = null,
    Guid? CurrentSessionId = null,
    string? IdpSessionToken = null,
    string? ConfirmNewPassword = null) : IRequest<ErrorOr<Success>>;
