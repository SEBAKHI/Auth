using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
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
        Domain.Entities.Application? application = null;
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
            if (application is null || !application.IsActive)
            {
                _logger.LogWarning(
                    "End-session request for unknown or inactive client {ClientId}", request.ClientId);
                return AuthErrors.InvalidClient;
            }
        }

        var postLogoutRedirectUri = ResolvePostLogoutRedirectUri(request, application);

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
                    ? BuildSignedOutUrl(request.ClientId, request.State)
                    : BuildReturnUrl(postLogoutRedirectUri, request.State)
            };
        }

        _logger.LogInformation(
            "End-session confirmation requested by client {ClientId} for user {UserId}",
            request.ClientId, session.UserId);

        // A live session always means the confirmation, whichever way the return
        // was asked for. The hash coming back while the session still lives is a
        // confirmation that did not revoke it, so it goes back to the question,
        // never to the relying party: a return there would tell it the session
        // ended when it did not.
        return new EndSessionResult
        {
            RedirectUrl = BuildConfirmationUrl(request.ClientId, request.State, postLogoutRedirectUri)
        };
    }

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

    private string BuildConfirmationUrl(string? clientId, string? state, string? postLogoutRedirectUri) =>
        BuildAccountsUrl("logout", clientId, state, postLogoutRedirectUri);

    private string BuildSignedOutUrl(string? clientId, string? state) =>
        BuildAccountsUrl("signed-out", clientId, state, postLogoutRedirectUri: null);

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
    /// </remarks>
    private string BuildAccountsUrl(string path, string? clientId, string? state, string? postLogoutRedirectUri)
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

        return query.Count == 0
            ? $"{accountsBase}/{path}"
            : $"{accountsBase}/{path}?{string.Join('&', query)}";
    }
}
