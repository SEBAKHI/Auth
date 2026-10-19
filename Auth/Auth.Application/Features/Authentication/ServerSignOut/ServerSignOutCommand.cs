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
/// The answer to a server-side sign-out: what this call did to the single sign-on session the
/// token is linked to. Snake_case, like the token endpoint's answer, since a server reads it beside
/// its other OAuth calls. Whatever it says, the application then sends the browser to the
/// end-session endpoint: only the browser's own cookie tells what is left there.
/// </summary>
public sealed record ServerSignOutResult
{
    /// <summary>
    /// True when this call ended the live IdP session the token is linked to. It says what this
    /// call did, never what the browser holds: a browser that lost its session cookie and signed in
    /// again holds another session, which no token names. False whenever this call ended no IdP
    /// session: the token carried no link, the linked session was already over, or the token is
    /// stale (then nothing ended here at all). Information for logs and metrics; the next step is
    /// the same either way.
    /// </summary>
    [JsonPropertyName("sso_session_ended")]
    public required bool SsoSessionEnded { get; init; }
}
