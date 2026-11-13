using Auth.Application.Common.BotDefense;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ForgotPassword;

/// <summary>
/// Command to initiate a password reset flow.
/// </summary>
/// <param name="Email">The email address of the user requesting a password reset.</param>
/// <param name="ClientIp">The caller's address, for the bot check's per-address allowance alone (S13).</param>
/// <param name="BotToken">The page's bot-check token (S13), or null when the request carried none.</param>
public record ForgotPasswordCommand(
    string Email,
    string? ClientIp = null,
    string? BotToken = null) : IRequest<ErrorOr<ForgotPasswordResponse>>, IBotProtectedRequest
{
    string IBotProtectedRequest.Action => BotDefenseActions.ForgotPassword;
}

/// <summary>
/// Response from the forgot password command.
/// </summary>
/// <param name="ExpiresAt">When the token expires.</param>
/// <param name="MaskedEmail">The masked email address for user feedback.</param>
public record ForgotPasswordResponse(DateTime ExpiresAt, string MaskedEmail);
