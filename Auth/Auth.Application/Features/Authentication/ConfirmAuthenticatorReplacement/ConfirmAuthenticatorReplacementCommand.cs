using Auth.Application.Features.Authentication.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ConfirmAuthenticatorReplacement;

/// <summary>
/// Command to finish moving the second factor to a new authenticator app: a code
/// from the NEW app confirms the secret <c>replace</c> issued, which then replaces
/// the current one, with a new set of recovery codes.
/// </summary>
/// <param name="UserId">The ID of the user.</param>
/// <param name="Code">A six-digit code from the new authenticator app.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>): measured by the
/// re-authentication check, and spared when the other sessions are signed out.
/// </param>
/// <param name="IdpSessionToken">
/// The caller's own SSO cookie value, if the browser presented one, spared when
/// the other browsers' SSO sessions are ended.
/// </param>
/// <param name="IpAddress">The caller's address, for the log lines only.</param>
public record ConfirmAuthenticatorReplacementCommand(
    Guid UserId,
    string Code,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? IpAddress) : IRequest<ErrorOr<TwoFactorRecoveryCodesResponse>>
{
    // The code and the SSO cookie are secrets; the synthesized ToString would
    // print them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"ConfirmAuthenticatorReplacementCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId} }}";
}
