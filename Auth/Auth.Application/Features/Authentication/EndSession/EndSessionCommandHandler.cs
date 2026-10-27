using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.EndSession;

/// <summary>
/// Decides where a relying party's logout request sends the browser: to the
/// confirmation screen when there is a session to end, or, when there is not,
/// back to the relying party's registered return address or else to the
/// signed-out page.
/// </summary>
/// <remarks>
/// Revokes nothing itself. The specification asks the provider to confirm with
/// the user whenever it cannot verify who is asking, and it cannot: verification
/// would come from an <c>id_token_hint</c>, which requires id tokens this
/// provider does not issue. Acting on the bare GET would mean any page anywhere
/// could sign our users out by loading this URL in an image tag.
/// <para>
/// The return (<c>post_logout_redirect_uri</c>) is the only destination off our
/// own origin, and it is reached only while no session is live: before the
/// confirmation, or after the confirmation revoked it. It must be registered,
/// exactly, for the application in <c>client_id</c>. Anything else about it is
/// ignored, never answered with an error, so a relying party that asks wrongly
/// gets the behaviour it would have had without asking, not a dead end.
/// </para>
/// <para>
/// The accounts page comes back with the address's hash (<c>plr</c>) only after
/// the person confirmed. Two answers there differ from a relying party's own
/// request. While the session still lives, the confirmation is asked once more
/// WITHOUT the hash, so a confirmation that ended nothing cannot lead round
/// again. And an application switched off or deleted in between is no error:
/// the return and the client are dropped, and the request goes on as one that
/// names no application, rather than stranding someone who has just signed out
/// on a 400. A relying party's own request with such a client keeps its 400.
/// </para>
/// </remarks>
public class EndSessionCommandHandler : IRequestHandler<EndSessionCommand, ErrorOr<EndSessionResult>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IIdpSessionReader _idpSessionReader;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<EndSessionCommandHandler> _logger;

    public EndSessionCommandHandler(
        IApplicationRepository applicationRepository,
        IIdpSessionReader idpSessionReader,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        ILogger<EndSessionCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _idpSessionReader = idpSessionReader;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<EndSessionResult>> Handle(
        EndSessionCommand request, CancellationToken cancellationToken)
    {
        // client_id is OPTIONAL in RP-Initiated Logout, and requiring it would
        // have recreated the very fault this endpoint was built to fix: an address
        // the discovery document advertises that a conformant client cannot use.
        // A logout request that names nobody is legal and must work.
        //
        // Present, though, it has to name a real and active application: a
        // caller inventing a client id is malformed, the name is what the
        // confirmation screen shows the user to tell them WHICH application is
        // asking them to sign out, and its list is the only one a return
        // address can come from.
        // How the accounts pages of this sign-out look; never a reason to refuse it.
        var hints = PresentationHints.From(request.UiLocales, request.UiTheme);

        Domain.Entities.Application? application = null;
        var clientId = request.ClientId;
        var returnDropped = false;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            application = await _applicationRepository.GetByCodeAsync(clientId, cancellationToken);
            if (application is null || !application.IsActive)
            {
                if (!ArrivesByHashOnly(request))
                {
                    _logger.LogWarning(
                        "End-session request for unknown or inactive client {ClientId}", clientId);
                    return AuthErrors.InvalidClient;
                }

                // Only the accounts page sends the hash alone, after the person
                // confirmed: the application went away in between. Go on as a
                // request that names no application.
                _logger.LogWarning(
                    "End-session return dropped for unknown or inactive client {ClientId}; continuing without a client",
                    clientId);
                application = null;
                clientId = null;
                returnDropped = true;
            }
        }

        var postLogoutRedirectUri = returnDropped
            ? null
            : ResolvePostLogoutRedirectUri(request, application);

        // A session that went idle is no session (OI-154). Asking whether to sign
        // out is not a use of it, so the read records none.
        var session = await _idpSessionReader.FindLiveAsync(
            request.IdpSessionToken, IdpSessionReadMode.Check, cancellationToken);
        if (session is null)
        {
            // Nothing to end. Not an error: a second click, a refresh, or a tab
            // left open since the session expired all arrive here, and all of
            // them are already in the state they asked for. So does the accounts
            // page once the confirmation has revoked the session, and only then
            // is the relying party told, by being sent back, that it is over.
            return new EndSessionResult
            {
                RedirectUrl = postLogoutRedirectUri is null
                    ? BuildSignedOutUrl(clientId, request.State, hints)
                    : BuildReturnUrl(postLogoutRedirectUri, request.State)
            };
        }

        _logger.LogInformation(
            "End-session confirmation requested by client {ClientId} for user {UserId}",
            clientId, session.UserId);

        // A live session always means the confirmation, never the relying party:
        // a return there would tell it the session ended when it did not. The
        // hash coming back while the session still lives is a confirmation that
        // ended nothing, so the question is asked once more without it, and the
        // next confirmation ends on the signed-out page instead of going round.
        return new EndSessionResult
        {
            RedirectUrl = BuildConfirmationUrl(
                clientId,
                request.State,
                ArrivesByHashOnly(request) ? null : postLogoutRedirectUri,
                hints)
        };
    }

    /// <summary>
    /// Whether this is the accounts page coming back by the hash: <c>plr</c>
    /// without <c>post_logout_redirect_uri</c>, which no relying party sends.
    /// </summary>
    private static bool ArrivesByHashOnly(EndSessionCommand request) =>
        !string.IsNullOrEmpty(request.PostLogoutReturnHash) &&
        string.IsNullOrEmpty(request.PostLogoutRedirectUri);

    /// <summary>
    /// The registered return address this request may be sent back to, or null
    /// when it asked for none or asked in a way that cannot be honoured.
    /// </summary>
    /// <remarks>
    /// A refusal here is a logged warning and the behaviour of a request that
    /// asked for no return, never an error page: the user still gets the
    /// confirmation or the signed-out page.
    /// </remarks>
    private string? ResolvePostLogoutRedirectUri(
        EndSessionCommand request, Domain.Entities.Application? application)
    {
        var byAddress = !string.IsNullOrEmpty(request.PostLogoutRedirectUri);
        var byHash = !string.IsNullOrEmpty(request.PostLogoutReturnHash);
        if (!byAddress && !byHash)
        {
            return null;
        }

        // Both at once is no request this provider or a relying party builds.
        if (byAddress && byHash)
        {
            return IgnoreReturn(request.ClientId, "both the address and its hash were sent");
        }

        // The list belongs to an application, so without one there is nothing
        // to match against (RP-Initiated Logout requires client_id here).
        if (application is null)
        {
            return IgnoreReturn(request.ClientId, "no client_id");
        }

        // The longest state authorize accepts; a longer one is not carried to a
        // relying party from here either.
        if (request.State is { Length: > RelyingPartyRedirect.MaxStateLength })
        {
            return IgnoreReturn(request.ClientId, "state too long");
        }

        var registered = byAddress
            ? application.IsPostLogoutRedirectUriAllowed(request.PostLogoutRedirectUri!)
                ? request.PostLogoutRedirectUri
                : null
            : application.PostLogoutRedirectUris.FirstOrDefault(uri =>
                string.Equals(PostLogoutReturnHash.Of(uri), request.PostLogoutReturnHash, StringComparison.Ordinal));

        return registered ?? IgnoreReturn(request.ClientId, "not registered for this application");
    }

    private string? IgnoreReturn(string? clientId, string reason)
    {
        _logger.LogWarning(
            "End-session return ignored for client {ClientId}: {Reason}", clientId, reason);
        return null;
    }

    private string BuildConfirmationUrl(
        string? clientId, string? state, string? postLogoutRedirectUri, PresentationHints hints) =>
        BuildAccountsUrl("logout", clientId, state, postLogoutRedirectUri, hints);

    private string BuildSignedOutUrl(string? clientId, string? state, PresentationHints hints) =>
        BuildAccountsUrl("signed-out", clientId, state, postLogoutRedirectUri: null, hints);

    /// <summary>
    /// The relying party's registered address, with its state: the one
    /// destination off our own origin, and only ever one from the allowlist.
    /// </summary>
    private static string BuildReturnUrl(string postLogoutRedirectUri, string? state) =>
        string.IsNullOrEmpty(state)
            ? postLogoutRedirectUri
            : RelyingPartyRedirect.AppendQuery(postLogoutRedirectUri, "state", state);

    /// <summary>
    /// Builds a URL on the accounts application, the only origin this handler
    /// names apart from a registered return address.
    /// </summary>
    /// <remarks>
    /// state is carried through so the relying party can match the round trip,
    /// and is only ever a query value — never rendered, never interpreted here.
    /// A return address travels as its hash, never itself: an accounts URL whose
    /// query holds a whole address can be refused by a web application firewall.
    /// The presentation hints travel resolved, as on the sign-in pages.
    /// </remarks>
    private string BuildAccountsUrl(
        string path, string? clientId, string? state, string? postLogoutRedirectUri, PresentationHints hints)
    {
        var accountsBase = _idpSettings.AccountsBaseUrl.TrimEnd('/');

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            query.Add($"client_id={Uri.EscapeDataString(clientId)}");
        }

        if (!string.IsNullOrEmpty(state))
        {
            query.Add($"state={Uri.EscapeDataString(state)}");
        }

        if (postLogoutRedirectUri is not null)
        {
            query.Add($"{PostLogoutReturnHash.Parameter}={PostLogoutReturnHash.Of(postLogoutRedirectUri)}");
        }

        return PresentationHintsQuery.AppendTo(
            query.Count == 0
                ? $"{accountsBase}/{path}"
                : $"{accountsBase}/{path}?{string.Join('&', query)}",
            hints);
    }
}
