using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyEmail;

/// <summary>
/// Command to verify an email using OTP. The target user is identified by
/// <paramref name="UserId"/> when known (admin flows) or by <paramref name="Email"/>
/// for anonymous flows such as registration and the login page. The anonymous
/// path signs the user in on success, so the client details are captured for the
/// issued session.
/// </summary>
/// <param name="UserId">The ID of the user, when known.</param>
/// <param name="Otp">The 6-digit OTP code.</param>
/// <param name="Email">The email address of the user, when the ID is not known.</param>
/// <param name="IpAddress">The client's IP address, used when issuing the session on the anonymous path.</param>
/// <param name="UserAgent">The client's user agent, used when issuing the session on the anonymous path.</param>
/// <param name="DeviceId">The client's device identifier, used when issuing the session on the anonymous path.</param>
public record VerifyEmailCommand(
    Guid? UserId,
    string Otp,
    string? Email = null,
    string? IpAddress = null,
    string? UserAgent = null,
    string? DeviceId = null) : IRequest<ErrorOr<VerifyEmailResult>>;
