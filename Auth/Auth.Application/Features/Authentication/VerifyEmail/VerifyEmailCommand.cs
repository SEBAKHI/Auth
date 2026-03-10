using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Command to verify an email using OTP.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Otp">The 6-digit OTP code.</param>
public record VerifyEmailCommand(Guid UserId, string Otp) : IRequest<ErrorOr<Success>>;
