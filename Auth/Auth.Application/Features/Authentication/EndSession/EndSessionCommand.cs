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
/// <param name="PostLogoutRedirectUri">
/// Where the relying party asks the browser to be sent back once signed out
/// (<c>post_logout_redirect_uri</c>). Honoured only when it is registered,
/// exactly, for the application in <paramref name="ClientId"/>.
/// </param>
/// <param name="PostLogoutReturnHash">
/// The same request coming back from the accounts logout page, which names the
/// registered address by its hash instead of carrying it
/// (<see cref="EndSession.PostLogoutReturnHash"/>).
/// </param>
/// <param name="UiLocales">
/// The <c>ui_locales</c> parameter (RP-Initiated Logout §2): the languages the
/// accounts logout pages should show in. An unusable value is dropped.
/// </param>
/// <param name="UiTheme">
/// The <c>ui_theme</c> parameter: <c>light</c>, <c>dark</c> or <c>system</c>.
/// </param>
public record EndSessionCommand(
    string? ClientId,
    string? State,
    string? IdpSessionToken,
    string? PostLogoutRedirectUri = null,
    string? PostLogoutReturnHash = null,
    string? UiLocales = null,
    string? UiTheme = null) : IRequest<ErrorOr<EndSessionResult>>;

/// <summary>
/// Where the browser goes next.
/// </summary>
/// <remarks>
/// One field, on purpose. Whether a confirmation is still needed is already
/// carried by WHICH page the URL names, and a second field restating it would be
/// a fact that can disagree with itself.
/// <para>
/// Arriving with nothing to revoke is not an error and must not look like one: a
/// second click, a refresh, or a stale tab all land on the signed-out page,
/// because all of them are already in the state they asked for.
/// </para>
/// </remarks>
public record EndSessionResult
{
    /// <summary>
    /// Gets the absolute URL to redirect the browser to.
    /// </summary>
    public required string RedirectUrl { get; init; }

}
