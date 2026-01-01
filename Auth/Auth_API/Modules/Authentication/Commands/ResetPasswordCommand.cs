using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Command to reset a user's password using a reset token.
/// </summary>
/// <param name="Token">The password reset token.</param>
/// <param name="NewPassword">The new password to set.</param>
public record ResetPasswordCommand(
    string Token,
    string NewPassword) : IRequest<ErrorOr<Success>>;
