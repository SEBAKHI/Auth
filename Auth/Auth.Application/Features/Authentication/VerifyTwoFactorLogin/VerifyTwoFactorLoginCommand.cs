using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Command to complete a two-factor login by verifying a TOTP code or a
/// recovery code against a pending login challenge.
/// </summary>
public record VerifyTwoFactorLoginCommand(
    string ChallengeToken,
    string Code,
    bool UseRecoveryCode,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null) : IRequest<ErrorOr<LoginResponse>>;
