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
/// <param name="ExpiresAt">When the token expires.</param>
/// <param name="MaskedEmail">The masked email address for user feedback.</param>
public record ForgotPasswordResponse(DateTime ExpiresAt, string MaskedEmail);
