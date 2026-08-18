using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.EndSession;

/// <summary>
/// OIDC RP-Initiated Logout: a relying party asking us to end the single
/// sign-on session it cannot reach itself.
/// </summary>
/// <remarks>
/// Distinct from <c>LogoutCommand</c>, which serves our own applications over an
/// authenticated API call. This one arrives as a browser navigation with no
/// bearer token at all — only the SSO cookie — because that is what the
/// specification defines: the relying party redirects the user agent here.
/// <para>
/// No <c>id_token_hint</c> is accepted, because this provider issues no id
/// tokens. That single fact drives the whole shape of the flow: without one we
/// cannot verify that the request came from the relying party rather than from
/// any page that managed to put our URL in an image tag, so the user is asked
/// before anything is revoked.
/// </para>
/// </remarks>
/// <param name="ClientId">The relying party asking. Must be a known, active application.</param>
/// <param name="State">Opaque value the relying party gets back unchanged, so it can match the response to its request.</param>
/// <param name="IdpSessionToken">The plain SSO cookie value, if the browser holds one.</param>
public record EndSessionCommand(
    string? ClientId,
    string? State,
    string? IdpSessionToken) : IRequest<ErrorOr<EndSessionResult>>;

/// <summary>
/// Where the browser goes next, and whether anything still needs confirming.
/// </summary>
public record EndSessionResult
{
    /// <summary>
    /// Gets the absolute URL to redirect the browser to.
    /// </summary>
    public required string RedirectUrl { get; init; }

    /// <summary>
    /// Gets whether the browser is being sent to ask the user, rather than to
    /// the finished-signing-out page.
    /// </summary>
    /// <remarks>
    /// False when there was no live session to end. Arriving with nothing to
    /// revoke is not an error and must not look like one: a second click, a
    /// refresh, or a stale tab all land here, and all of them should simply see
    /// that they are signed out.
    /// </remarks>
    public bool RequiresConfirmation { get; init; }
}
