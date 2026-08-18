using Auth.Application.Configuration;
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
/// confirmation screen when there is a session to end, or straight to the
/// signed-out page when there is not.
/// </summary>
/// <remarks>
/// Revokes nothing itself. The specification asks the provider to confirm with
/// the user whenever it cannot verify who is asking, and it cannot: verification
/// would come from an <c>id_token_hint</c>, which requires id tokens this
/// provider does not issue. Acting on the bare GET would mean any page anywhere
/// could sign our users out by loading this URL in an image tag.
/// </remarks>
public class EndSessionCommandHandler : IRequestHandler<EndSessionCommand, ErrorOr<EndSessionResult>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<EndSessionCommandHandler> _logger;

    public EndSessionCommandHandler(
        IApplicationRepository applicationRepository,
        IIdpSessionRepository idpSessionRepository,
        IRefreshTokenKeyService refreshTokenKeyService,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        ILogger<EndSessionCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _idpSessionRepository = idpSessionRepository;
        _refreshTokenKeyService = refreshTokenKeyService;
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
        // Present, though, it has to name a real and active application. There is
        // no open redirect to guard against — every destination below is on our
        // own origin — but a caller inventing a client id is malformed, and the
        // name is what the confirmation screen shows the user to tell them WHICH
        // application is asking them to sign out.
        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            var application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
            if (application is null || !application.IsActive)
            {
                _logger.LogWarning(
                    "End-session request for unknown or inactive client {ClientId}", request.ClientId);
                return AuthErrors.InvalidClient;
            }
        }

        var session = await ResolveSessionAsync(request.IdpSessionToken, cancellationToken);
        if (session is null)
        {
            // Nothing to end. Not an error: a second click, a refresh, or a tab
            // left open since the session expired all arrive here, and all of
            // them are already in the state they asked for.
            return new EndSessionResult
            {
                RedirectUrl = BuildSignedOutUrl(request.ClientId, request.State),
                RequiresConfirmation = false
            };
        }

        _logger.LogInformation(
            "End-session confirmation requested by client {ClientId} for user {UserId}",
            request.ClientId, session.UserId);

        return new EndSessionResult
        {
            RedirectUrl = BuildConfirmationUrl(request.ClientId, request.State),
            RequiresConfirmation = true
        };
    }

    private async Task<Auth.Domain.Entities.IdpSession?> ResolveSessionAsync(
        string? idpSessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idpSessionToken))
        {
            return null;
        }

        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(idpSessionToken);
        var session = await _idpSessionRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        return session is not null && session.IsValid() ? session : null;
    }

    private string BuildConfirmationUrl(string? clientId, string? state) =>
        BuildAccountsUrl("logout", clientId, state);

    private string BuildSignedOutUrl(string? clientId, string? state) =>
        BuildAccountsUrl("signed-out", clientId, state);

    /// <summary>
    /// Builds a URL on the accounts application, the only origin this handler
    /// ever names.
    /// </summary>
    /// <remarks>
    /// state is carried through so the relying party can match the round trip,
    /// and is only ever a query value — never rendered, never interpreted here.
    /// </remarks>
    private string BuildAccountsUrl(string path, string? clientId, string? state)
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

        return query.Count == 0
            ? $"{accountsBase}/{path}"
            : $"{accountsBase}/{path}?{string.Join('&', query)}";
    }
}
