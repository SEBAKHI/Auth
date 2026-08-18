using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// Handles the OAuth 2.0 authorize request. Per RFC 6749 §4.1.2.1, failures are
/// split in two: an unknown client or unregistered redirect_uri returns an
/// error WITHOUT redirecting (the redirect target cannot be trusted), while all
/// later failures redirect back to the validated redirect_uri with an OAuth
/// error code. Without a valid IdP session the browser is sent to the accounts
/// login page carrying the original authorize URL as returnTo.
/// <para>
/// A signed-in user who is not entitled to the application gets the redirecting
/// kind: <c>error=access_denied</c>, back to the already-validated redirect_uri.
/// The client needs to tell "not signed in" from "signed in but not allowed here"
/// to show the right message, and the only non-redirecting alternative available
/// is the login bounce — which would loop forever, since the user signs in
/// successfully every time and is refused again on return.
/// </para>
/// </summary>
public class AuthorizeCommandHandler : IRequestHandler<AuthorizeCommand, ErrorOr<AuthorizeResult>>
{
    // RFC 7636: base64url characters, 43 chars for an S256 challenge
    // (SHA-256 output) with headroom up to the 128 the spec allows verifiers.
    private static readonly Regex CodeChallengePattern = new(
        "^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private const int MaxStateLength = 512;

    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IAuthorizationCodeRepository _authorizationCodeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IStepUpTicketService _stepUpTicketService;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<AuthorizeCommandHandler> _logger;

    public AuthorizeCommandHandler(
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IIdpSessionRepository idpSessionRepository,
        IAuthorizationCodeRepository authorizationCodeRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IStepUpTicketService stepUpTicketService,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        ILogger<AuthorizeCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _idpSessionRepository = idpSessionRepository;
        _authorizationCodeRepository = authorizationCodeRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _stepUpTicketService = stepUpTicketService;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<AuthorizeResult>> Handle(AuthorizeCommand request, CancellationToken cancellationToken)
    {
        // --- Hard failures: no redirect target may be trusted yet ---

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            return AuthErrors.InvalidClient;
        }

        var application = await _applicationRepository.GetByCodeAsync(request.ClientId, cancellationToken);
        if (application is null || !application.IsActive)
        {
            _logger.LogWarning("Authorize request for unknown or inactive client {ClientId}", request.ClientId);
            return AuthErrors.InvalidClient;
        }

        if (string.IsNullOrWhiteSpace(request.RedirectUri) ||
            !application.IsRedirectUriAllowed(request.RedirectUri))
        {
            _logger.LogWarning(
                "Authorize request for client {ClientId} with unregistered redirect_uri", request.ClientId);
            return AuthErrors.InvalidRedirectUri;
        }

        // --- Soft failures: redirect back to the validated redirect_uri ---

        if (!string.Equals(request.ResponseType, "code", StringComparison.Ordinal))
        {
            return ErrorRedirect(request, "unsupported_response_type");
        }

        if (request.State is { Length: > MaxStateLength })
        {
            return ErrorRedirect(request, "invalid_request");
        }

        if (string.IsNullOrWhiteSpace(request.CodeChallenge) ||
            !CodeChallengePattern.IsMatch(request.CodeChallenge))
        {
            return ErrorRedirect(request, "invalid_request");
        }

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal))
        {
            return ErrorRedirect(request, "invalid_request");
        }

        if (!TryParsePrompt(request.Prompt, out var prompt))
        {
            return ErrorRedirect(request, "invalid_request");
        }

        // --- IdP session: no valid session means the browser goes to login ---

        var session = await ResolveSessionAsync(request.IdpSessionToken, cancellationToken);
        if (session is null)
        {
            return LoginRequired(request, prompt, demandStepUp: false);
        }

        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || user.IsLockedOut())
        {
            // Force a fresh interactive login instead of leaking account state.
            return LoginRequired(request, prompt, demandStepUp: false);
        }

        // --- Step-up: the authentication behind this session must be fresh ---
        // A valid session exists, but the app's policy (ReauthenticationMaxAgeMinutes)
        // or the request's prompt=login / max_age may demand a fresher one.
        var stepUp = EvaluateStepUp(request, application, session, prompt);
        if (stepUp.Required)
        {
            _logger.LogInformation(
                "Step-up re-authentication required for client {ClientId} and user {UserId}",
                request.ClientId, user.Id);

            return LoginRequired(request, prompt, demandStepUp: true);
        }

        // --- Entitlement: is this user allowed into THIS application? ---
        // Last, on purpose. A user whose session is too old re-authenticates
        // before being told no, which costs one extra round trip in the rare
        // step-up case and buys this: a stolen stale session cookie cannot be
        // used to enumerate which applications its owner may enter.
        if (!await _applicationAccessRepository.IsUserEntitledAsync(user.Id, application.Id, cancellationToken))
        {
            // No error_description: the client is told it was refused, never why
            // or about whom. The detail belongs in the server log.
            _logger.LogWarning(
                "Access denied for user {UserId} to client {ClientId}", user.Id, request.ClientId);

            return ErrorRedirect(request, "access_denied");
        }

        // --- Issue the one-time code bound to this exact request ---

        var plainCode = _jwtTokenService.GenerateRefreshToken();
        var code = AuthorizationCode.Create(
            application.Id,
            user.Id,
            _refreshTokenKeyService.ComputeTokenHash(plainCode),
            request.RedirectUri,
            request.CodeChallenge,
            _idpSettings.AuthorizationCodeLifetime,
            request.IpAddress);

        await _authorizationCodeRepository.CreateAsync(code, cancellationToken);

        _logger.LogInformation(
            "Issued authorization code for client {ClientId} and user {UserId}",
            request.ClientId, user.Id);

        return new AuthorizeResult
        {
            RedirectUrl = AppendQuery(request.RedirectUri, "code", plainCode, request.State),
            // The demand has been answered, so spend the ticket. Leaving it to
            // expire on its own would let one re-authentication satisfy every
            // prompt=login this client made within the ticket's lifetime.
            ClearStepUpTicket = stepUp.TicketSpent
        };
    }

    /// <summary>
    /// The response for "this browser has to authenticate (again)": normally a
    /// redirect to the hosted login page, carrying a fresh step-up ticket when a
    /// demand is being raised.
    /// </summary>
    /// <remarks>
    /// Under <c>prompt=none</c> the relying party has forbidden any UI, so the
    /// answer is the OIDC error instead of a redirect the client cannot show.
    /// This is what a hidden-iframe silent renewal needs: previously it received a
    /// 302 to the login page and rendered it inside the frame.
    /// </remarks>
    private AuthorizeResult LoginRequired(AuthorizeCommand request, PromptValues prompt, bool demandStepUp)
    {
        if (prompt.None)
        {
            return ErrorRedirect(request, "login_required");
        }

        return new AuthorizeResult
        {
            RedirectUrl = BuildLoginRedirect(request.OriginalRequestUrl),
            IsLoginRedirect = true,
            StepUpTicketToSet = demandStepUp
                ? _stepUpTicketService.Issue(request.ClientId!, DateTime.UtcNow)
                : null
        };
    }

    /// <summary>
    /// Parses the OIDC <c>prompt</c> parameter — a space-delimited list, matched
    /// case-insensitively.
    /// </summary>
    /// <returns>
    /// False when the combination is invalid, which the caller answers with
    /// <c>invalid_request</c>.
    /// </returns>
    private static bool TryParsePrompt(string? prompt, out PromptValues values)
    {
        values = default;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return true;
        }

        var tokens = prompt.Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var none = false;
        var login = false;

        foreach (var token in tokens)
        {
            if (token.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                none = true;
            }
            else if (token.Equals("login", StringComparison.OrdinalIgnoreCase))
            {
                login = true;
            }

            // consent and select_account are recognised OIDC values this provider
            // has no screen for, and unknown values may be client extensions.
            // Both are ignored rather than rejected, as before.
        }

        // OIDC Core §3.1.2.1: "none" may not be combined with any other value.
        if (none && tokens.Length > 1)
        {
            return false;
        }

        values = new PromptValues(none, login);
        return true;
    }

    /// <summary>
    /// Which of the OIDC prompt values this request carries.
    /// </summary>
    private readonly record struct PromptValues(bool None, bool Login);

    /// <summary>
    /// The outcome of the freshness check: whether to demand re-authentication,
    /// and whether a step-up ticket was consumed proving one already happened.
    /// </summary>
    private readonly record struct StepUpDecision(bool Required, bool TicketSpent)
    {
        public static StepUpDecision Demand() => new(Required: true, TicketSpent: false);
    }

    /// <summary>
    /// Decides whether a valid SSO session must nonetheless re-authenticate.
    /// Combines the per-app policy (ReauthenticationMaxAgeMinutes, admin-configured,
    /// disabled by default) with the per-request OIDC parameters prompt=login and
    /// max_age. When both a policy and a max_age apply, the more restrictive
    /// (smaller) window wins.
    /// </summary>
    /// <remarks>
    /// <c>prompt=login</c> is satisfied by EVIDENCE, never by the absence of the
    /// parameter. The evidence is a step-up ticket this server signed on the trip
    /// that raised the demand, plus a session minted after that moment — which
    /// only an interactive sign-in produces, since every sign-in inserts a new
    /// session row rather than refreshing the old one.
    /// <para>
    /// Before this, the branch returned true on the literal string and the accounts
    /// app deleted the parameter to stop the browser looping. That made the demand
    /// self-cancelling: anyone holding a live session cookie could delete the
    /// parameter by hand and be issued a code against the stale session, which is
    /// exactly the person step-up exists to stop.
    /// </para>
    /// </remarks>
    private StepUpDecision EvaluateStepUp(
        AuthorizeCommand request,
        Auth.Domain.Entities.Application application,
        IdpSession session,
        PromptValues prompt)
    {
        var now = DateTime.UtcNow;
        var ticketSpent = false;

        if (prompt.Login)
        {
            var proven =
                _stepUpTicketService.TryValidate(
                    request.StepUpTicket,
                    request.ClientId!,
                    now,
                    _idpSettings.StepUpTicketLifetime,
                    out var demandedAtUtc)
                && session.CreatedAt >= demandedAtUtc;

            if (!proven)
            {
                return StepUpDecision.Demand();
            }

            // Proved. max_age still applies below — a client may ask for both, and
            // the stricter of the two must win.
            ticketSpent = true;
        }

        int? maxAgeSeconds = null;

        if (application.ReauthenticationMaxAgeMinutes is int policyMinutes)
        {
            maxAgeSeconds = policyMinutes * 60;
        }

        if (TryParseMaxAgeSeconds(request.MaxAge, out var requestSeconds))
        {
            maxAgeSeconds = maxAgeSeconds is int existing
                ? Math.Min(existing, requestSeconds)
                : requestSeconds;
        }

        if (maxAgeSeconds is not int thresholdSeconds)
        {
            return new StepUpDecision(Required: false, ticketSpent);
        }

        var sessionAgeSeconds = (now - session.CreatedAt).TotalSeconds;
        return sessionAgeSeconds > thresholdSeconds
            ? StepUpDecision.Demand()
            : new StepUpDecision(Required: false, ticketSpent);
    }

    /// <summary>
    /// Parses the OIDC max_age parameter: a non-negative integer number of seconds.
    /// A malformed or negative value is ignored (treated as absent) rather than
    /// failing the request.
    /// </summary>
    private static bool TryParseMaxAgeSeconds(string? maxAge, out int seconds)
    {
        seconds = 0;
        return !string.IsNullOrWhiteSpace(maxAge)
            && int.TryParse(maxAge, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out seconds)
            && seconds >= 0;
    }

    private async Task<IdpSession?> ResolveSessionAsync(string? idpSessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idpSessionToken))
        {
            return null;
        }

        var tokenHash = _refreshTokenKeyService.ComputeTokenHash(idpSessionToken);
        var session = await _idpSessionRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
        return session is not null && session.IsValid() ? session : null;
    }

    private string BuildLoginRedirect(string originalRequestUrl)
    {
        var accountsBase = _idpSettings.AccountsBaseUrl.TrimEnd('/');
        return $"{accountsBase}/login?returnTo={Uri.EscapeDataString(originalRequestUrl)}";
    }

    private static AuthorizeResult ErrorRedirect(AuthorizeCommand request, string oauthError)
    {
        return new AuthorizeResult
        {
            RedirectUrl = AppendQuery(request.RedirectUri!, "error", oauthError, request.State)
        };
    }

    private static string AppendQuery(string redirectUri, string key, string value, string? state)
    {
        var separator = redirectUri.Contains('?') ? '&' : '?';
        var url = $"{redirectUri}{separator}{key}={Uri.EscapeDataString(value)}";

        if (!string.IsNullOrEmpty(state))
        {
            url += $"&state={Uri.EscapeDataString(state)}";
        }

        return url;
    }
}
