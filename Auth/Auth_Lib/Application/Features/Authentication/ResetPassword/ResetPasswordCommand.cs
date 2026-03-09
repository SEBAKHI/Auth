using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.ResetPassword;

/// <summary>
/// Command to reset a user's password using a reset token.
/// </summary>
/// <param name="Email">The user's email address for token lookup.</param>
/// <param name="Token">The password reset token.</param>
/// <param name="NewPassword">The new password to set.</param>
/// <param name="TerminateSessions">
/// Whether to terminate all sessions after password reset.
/// Null means use server configuration default.
/// </param>
public record ResetPasswordCommand(
    string Email,
    string Token,
    string NewPassword,
    bool? TerminateSessions = null) : IRequest<ErrorOr<Success>>;
