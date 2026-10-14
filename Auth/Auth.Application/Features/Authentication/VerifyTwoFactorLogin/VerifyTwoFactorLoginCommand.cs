using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Command to complete a two-factor login by verifying a TOTP code or a
/// recovery code against a pending login challenge.
/// </summary>
/// <param name="StaySignedIn">
/// The person ticked "Stay signed in" on the sign-in page that opened the
/// challenge (OI-154); the app carries it here in memory. Absent, after a reload
/// of the second-factor page for instance, means a browser session.
/// </param>
public record VerifyTwoFactorLoginCommand(
    string ChallengeToken,
    string Code,
    bool UseRecoveryCode,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null,
    bool StaySignedIn = false) : IRequest<ErrorOr<LoginResponse>>;
