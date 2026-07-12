using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Command to verify an email using OTP. The target user is identified by
/// <paramref name="UserId"/> when known (admin flows) or by <paramref name="Email"/>
/// for anonymous flows such as the login page.
/// </summary>
/// <param name="UserId">The ID of the user, when known.</param>
/// <param name="Otp">The 6-digit OTP code.</param>
/// <param name="Email">The email address of the user, when the ID is not known.</param>
public record VerifyEmailCommand(Guid? UserId, string Otp, string? Email = null) : IRequest<ErrorOr<Success>>;
