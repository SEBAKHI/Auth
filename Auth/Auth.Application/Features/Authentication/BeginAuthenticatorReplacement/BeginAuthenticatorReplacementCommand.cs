using Auth.Application.Features.Authentication.SetupTwoFactor;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.BeginAuthenticatorReplacement;

/// <summary>
/// Command to start moving an enabled second factor to a new authenticator app:
/// proves the current factor once more and issues the new secret, which waits
/// for a code from the new app (<c>replace/confirm</c>). The current app keeps
/// working until then.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">
/// A code from the CURRENT authenticator app, or one of the recovery codes when
/// <paramref name="UseRecoveryCode"/> is true — the way back for a lost phone.
/// </param>
/// <param name="UseRecoveryCode">Whether <paramref name="Code"/> is a recovery code.</param>
/// <param name="CurrentSessionId">The session the access token belongs to (its <c>sid</c>).</param>
/// <param name="IpAddress">The caller's address, for the reused-code log lines only.</param>
public record BeginAuthenticatorReplacementCommand(
    Guid UserId,
    string Code,
    bool UseRecoveryCode,
    Guid? CurrentSessionId,
    string? IpAddress) : IRequest<ErrorOr<TwoFactorSetupResponse>>
{
    // The code is a secret; the synthesized ToString would print it into any log
    // line or assertion message the command reaches.
    public override string ToString() =>
        $"BeginAuthenticatorReplacementCommand {{ UserId = {UserId}, UseRecoveryCode = {UseRecoveryCode}, CurrentSessionId = {CurrentSessionId} }}";
}
