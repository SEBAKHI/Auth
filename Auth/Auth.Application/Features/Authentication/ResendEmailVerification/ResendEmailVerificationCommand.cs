using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ResendEmailVerification;

/// <summary>
/// Command to resend email verification OTP.
/// </summary>
/// <param name="Email">The email address to resend verification to.</param>
public record ResendEmailVerificationCommand(string Email) : IRequest<ErrorOr<ResendEmailVerificationResponse>>;

/// <summary>
/// Response for resend email verification command.
/// </summary>
/// <param name="ExpiresAt">When the OTP expires.</param>
/// <param name="MaskedEmail">The masked email address for display.</param>
public record ResendEmailVerificationResponse(DateTime ExpiresAt, string MaskedEmail);
