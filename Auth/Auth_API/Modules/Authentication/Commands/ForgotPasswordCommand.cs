using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Command to initiate a password reset flow.
/// </summary>
/// <param name="Email">The email address of the user requesting a password reset.</param>
public record ForgotPasswordCommand(string Email) : IRequest<ErrorOr<ForgotPasswordResponse>>;

/// <summary>
/// Response from the forgot password command.
/// </summary>
/// <param name="Token">The reset token (in production, this would be sent via email).</param>
/// <param name="ExpiresAt">When the token expires.</param>
public record ForgotPasswordResponse(string Token, DateTime ExpiresAt);
