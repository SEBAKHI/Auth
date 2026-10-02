using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.EnableTwoFactor;

/// <summary>
/// Command to enable two-factor authentication after verifying a code.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">The TOTP code to verify.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>), measured by the
/// re-authentication check.
/// </param>
/// <param name="IpAddress">The caller's address, for the reused-code log lines only.</param>
public record EnableTwoFactorCommand(
    Guid UserId,
    string Code,
    Guid? CurrentSessionId,
    string? IpAddress) : IRequest<ErrorOr<EnableTwoFactorResponse>>
{
    // The code is a secret for its 90 seconds; the synthesized ToString would
    // print it into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"EnableTwoFactorCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId} }}";
}

/// <summary>
/// Response containing recovery codes.
/// </summary>
public record EnableTwoFactorResponse(string[] RecoveryCodes);
