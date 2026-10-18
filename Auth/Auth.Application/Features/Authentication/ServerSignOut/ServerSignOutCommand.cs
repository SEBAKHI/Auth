using System.Text.Json.Serialization;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ServerSignOut;

/// <summary>
/// A server-side application signs a person out from its server (OI-183): with its sign-in key
/// (<c>private_key_jwt</c>) and the refresh token of the person's session, which must be bound to
/// that key (OI-171). Ends the application's session and the IdP session the token is linked to,
/// so no confirmation page is needed in the browser.
/// </summary>
/// <param name="RefreshToken">The session's refresh token, bound to the application's sign-in key.</param>
/// <param name="ClientId">The form's <c>client_id</c>, when sent; it must then name the key's application.</param>
/// <param name="ClientAssertionType">The form's <c>client_assertion_type</c>.</param>
/// <param name="ClientAssertion">The assertion signed with the application's sign-in key.</param>
/// <param name="IpAddress">The caller's address, for the log.</param>
public record ServerSignOutCommand(
    string RefreshToken,
    string? ClientId,
    string? ClientAssertionType,
    string? ClientAssertion,
    string? IpAddress) : IRequest<ErrorOr<ServerSignOutResult>>;

/// <summary>
/// The answer to a server-side sign-out: whether the person's single sign-on session in the
/// browser they signed in from is over. Snake_case, like the token endpoint's answer, since a
/// server reads it beside its other OAuth calls.
/// </summary>
public sealed record ServerSignOutResult
{
    /// <summary>
    /// True when this call ended the live IdP session the token is linked to: the browser has no
    /// SSO session left from that sign-in, and no page is needed. False whenever this call ended no
    /// IdP session (the token carried no link, the linked session was already over, or the token
    /// is stale): the application then sends the browser to the end-session endpoint, which reads
    /// the browser's own cookie and asks only if a session is left there. A linked session already
    /// over says nothing of the browser, which may hold a newer one.
    /// </summary>
    [JsonPropertyName("sso_session_ended")]
    public required bool SsoSessionEnded { get; init; }
}
