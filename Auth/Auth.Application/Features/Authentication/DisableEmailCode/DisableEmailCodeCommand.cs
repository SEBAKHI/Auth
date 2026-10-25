using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.DisableEmailCode;

/// <summary>
/// Command to switch the emailed second factor off (S24). Without a code it emails
/// one — the code that lets the emailed code go, which also serves replacing it by an
/// authenticator app; with an emailed code or a recovery code it switches it off.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="Code">
/// The code emailed for switching off, or a recovery code when
/// <paramref name="UseRecoveryCode"/> is true; null or empty to ask for an email.
/// </param>
/// <param name="UseRecoveryCode">Whether <paramref name="Code"/> is a recovery code.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>): measured by the
/// re-authentication check, and spared when the other sessions are signed out.
/// </param>
/// <param name="IdpSessionToken">
/// The caller's own SSO cookie value, if the browser presented one, spared when the
/// other browsers' SSO sessions are ended.
/// </param>
/// <param name="IpAddress">The caller's address, named in the email.</param>
public record DisableEmailCodeCommand(
    Guid UserId,
    string? Code,
    bool UseRecoveryCode,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? IpAddress) : IRequest<ErrorOr<DisableEmailCodeResult>>
{
    // The code and the SSO cookie are secrets; the synthesized ToString would print
    // them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"DisableEmailCodeCommand {{ UserId = {UserId}, UseRecoveryCode = {UseRecoveryCode}, CurrentSessionId = {CurrentSessionId} }}";
}

/// <summary>
/// What the request did.
/// </summary>
/// <param name="CodeSent">
/// Where the code went, when the request asked for one; null when the emailed code
/// was switched off.
/// </param>
public record DisableEmailCodeResult(EmailCodeSent? CodeSent);
