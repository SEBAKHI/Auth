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
/// <param name="EmailCode">
/// The code emailed to the account's confirmed address, which an account binding its
/// FIRST second factor must also present while the email proof is required; null
/// when the client sent none.
/// </param>
public record EnableTwoFactorCommand(
    Guid UserId,
    string Code,
    Guid? CurrentSessionId,
    string? IpAddress,
    string? EmailCode = null) : IRequest<ErrorOr<EnableTwoFactorResponse>>
{
    // Both codes are secrets while they live; the synthesized ToString would
    // print them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"EnableTwoFactorCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId} }}";
}

/// <summary>
/// Response containing recovery codes.
/// </summary>
public record EnableTwoFactorResponse(string[] RecoveryCodes);
