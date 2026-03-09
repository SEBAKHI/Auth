using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.EnableTwoFactor;

/// <summary>
/// Command to enable two-factor authentication after verifying a code.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">The TOTP code to verify.</param>
public record EnableTwoFactorCommand(
    Guid UserId,
    string Code) : IRequest<ErrorOr<EnableTwoFactorResponse>>;

/// <summary>
/// Response containing recovery codes.
/// </summary>
public record EnableTwoFactorResponse(string[] RecoveryCodes);
