using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.DisableTwoFactor;

/// <summary>
/// Command to disable two-factor authentication.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">
/// The code that proves the factor: from the authenticator app, or one of the
/// user's recovery codes when <paramref name="UseRecoveryCode"/> is true.
/// </param>
/// <param name="UseRecoveryCode">Whether <paramref name="Code"/> is a recovery code.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>): measured by the
/// re-authentication check, and spared when the other sessions are signed out.
/// </param>
/// <param name="IdpSessionToken">
/// The caller's own SSO cookie value, if the browser presented one, spared when
/// the other browsers' SSO sessions are ended.
/// </param>
/// <param name="IpAddress">The caller's address, for the reused-code log lines only.</param>
public record DisableTwoFactorCommand(
    Guid UserId,
    string Code,
    bool UseRecoveryCode,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? IpAddress) : IRequest<ErrorOr<Success>>
{
    // The code and the SSO cookie are secrets; the synthesized ToString would
    // print them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"DisableTwoFactorCommand {{ UserId = {UserId}, UseRecoveryCode = {UseRecoveryCode}, CurrentSessionId = {CurrentSessionId} }}";
}
