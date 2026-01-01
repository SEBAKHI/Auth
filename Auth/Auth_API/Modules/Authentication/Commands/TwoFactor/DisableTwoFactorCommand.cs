using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands.TwoFactor;

/// <summary>
/// Command to disable two-factor authentication.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">The TOTP code to verify (required to disable).</param>
public record DisableTwoFactorCommand(
    Guid UserId,
    string Code) : IRequest<ErrorOr<Success>>;
