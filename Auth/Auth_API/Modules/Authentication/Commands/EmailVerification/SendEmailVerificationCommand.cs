using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands.EmailVerification;

/// <summary>
/// Command to send an email verification OTP to a user.
/// </summary>
/// <param name="UserId">The ID of the user to send verification to.</param>
public record SendEmailVerificationCommand(Guid UserId) : IRequest<ErrorOr<SendEmailVerificationResponse>>;

/// <summary>
/// Response from the send email verification command.
/// </summary>
/// <param name="ExpiresAt">When the OTP expires.</param>
/// <param name="MaskedEmail">The email address the OTP was sent to (masked for privacy).</param>
public record SendEmailVerificationResponse(DateTime ExpiresAt, string MaskedEmail);
