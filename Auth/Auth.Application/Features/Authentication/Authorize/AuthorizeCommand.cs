using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// OAuth 2.0 authorization request (authorization-code + PKCE flow).
/// Raw string inputs are validated inside the handler because OAuth prescribes
/// which failures may redirect back to the client and which must not.
/// </summary>
/// <param name="ResponseType">The OAuth response_type; only "code" is supported.</param>
/// <param name="ClientId">The application code acting as the public client id.</param>
/// <param name="RedirectUri">The requested redirect URI (must be registered).</param>
/// <param name="CodeChallenge">The PKCE S256 code challenge.</param>
/// <param name="CodeChallengeMethod">The PKCE method; only "S256" is supported.</param>
/// <param name="State">Opaque client state echoed back on the redirect.</param>
/// <param name="IdpSessionToken">The plain IdP session cookie value, if present.</param>
/// <param name="OriginalRequestUrl">The full authorize URL, used as returnTo for the login redirect.</param>
/// <param name="IpAddress">The client's IP address.</param>
public record AuthorizeCommand(
    string? ResponseType,
    string? ClientId,
    string? RedirectUri,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? State,
    string? IdpSessionToken,
    string OriginalRequestUrl,
    string? IpAddress) : IRequest<ErrorOr<AuthorizeResult>>;

/// <summary>
/// Where the authorize endpoint should send the browser (always a 302).
/// </summary>
public record AuthorizeResult
{
    /// <summary>
    /// Gets the absolute URL to redirect the browser to: either the client's
    /// redirect URI (with code/error parameters) or the accounts login page.
    /// </summary>
    public required string RedirectUrl { get; init; }

    /// <summary>
    /// Gets whether the redirect targets the accounts login page because no
    /// valid IdP session was presented.
    /// </summary>
    public bool IsLoginRedirect { get; init; }
}
