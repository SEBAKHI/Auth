using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ConfirmEmailCode;

/// <summary>
/// Command to make the emailed code the account's second factor (S24), with the code
/// <c>EnableEmailCodeCommand</c> emailed to its address.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="Code">The six-digit code from the email.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>): measured by the
/// re-authentication check, and spared when the other sessions are signed out.
/// </param>
/// <param name="IdpSessionToken">
/// The caller's own SSO cookie value, if the browser presented one, spared when the
/// other browsers' SSO sessions are ended.
/// </param>
/// <param name="IpAddress">The caller's address.</param>
public record ConfirmEmailCodeCommand(
    Guid UserId,
    string Code,
    Guid? CurrentSessionId,
    string? IdpSessionToken,
    string? IpAddress) : IRequest<ErrorOr<EmailCodeConfirmedResponse>>
{
    // The code and the SSO cookie are secrets; the synthesized ToString would print
    // them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"ConfirmEmailCodeCommand {{ UserId = {UserId}, CurrentSessionId = {CurrentSessionId} }}";
}

/// <summary>
/// The emailed code is bound.
/// </summary>
/// <param name="RecoveryCodes">
/// The new recovery codes, shown once — issued only when the account had none; null
/// when the codes the account already held stay (they were issued with the
/// authenticator app the emailed code replaced).
/// </param>
public record EmailCodeConfirmedResponse(string[]? RecoveryCodes);
