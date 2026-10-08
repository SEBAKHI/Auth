using Auth.Application.Features.Authentication.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.RegenerateRecoveryCodes;

/// <summary>
/// Command to replace the recovery codes of an enabled second factor with a new
/// set, from a recent two-factor session and with one more proof of the factor.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">
/// A code from the authenticator app, or one of the current recovery codes when
/// <paramref name="UseRecoveryCode"/> is true.
/// </param>
/// <param name="UseRecoveryCode">Whether <paramref name="Code"/> is a recovery code.</param>
/// <param name="CurrentSessionId">The session the access token belongs to (its <c>sid</c>).</param>
/// <param name="IpAddress">The caller's address, for the reused-code log lines only.</param>
public record RegenerateRecoveryCodesCommand(
    Guid UserId,
    string Code,
    bool UseRecoveryCode,
    Guid? CurrentSessionId,
    string? IpAddress) : IRequest<ErrorOr<TwoFactorRecoveryCodesResponse>>
{
    // The code is a secret; the synthesized ToString would print it into any log
    // line or assertion message the command reaches.
    public override string ToString() =>
        $"RegenerateRecoveryCodesCommand {{ UserId = {UserId}, UseRecoveryCode = {UseRecoveryCode}, CurrentSessionId = {CurrentSessionId} }}";
}
