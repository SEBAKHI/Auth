using System.Text.RegularExpressions;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
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
/// login page carrying the client id and a handle to the request, which is
/// stored here until the page resumes it (OI-145); with
/// <see cref="IdentityProviderSettings.AuthorizationRequestHandlesEnabled"/> off,
/// it carries the original authorize URL as returnTo instead.
/// <para>
/// A resume (<see cref="ResumeAuthorizationCommand"/>) runs the same pipeline on
/// the stored values, every check included, so a client switched off or a
/// redirect URI unregistered since then fails exactly as a fresh request would.
/// </para>
/// <para>
/// A signed-in user who is not entitled to the application gets the redirecting
/// kind: <c>error=access_denied</c>, back to the already-validated redirect_uri.
/// The client needs to tell "not signed in" from "signed in but not allowed here"
/// to show the right message, and the only non-redirecting alternative available
/// is the login bounce — which would loop forever, since the user signs in
/// successfully every time and is refused again on return.
/// </para>
/// </summary>
public class AuthorizeCommandHandler :
    IRequestHandler<AuthorizeCommand, ErrorOr<AuthorizeResult>>,
    IRequestHandler<ResumeAuthorizationCommand, ErrorOr<AuthorizeResult>>
{
    // RFC 7636: base64url characters, 43 chars for an S256 challenge
    // (SHA-256 output) with headroom up to the 128 the spec allows verifiers.
    private static readonly Regex CodeChallengePattern = new(
        "^[A-Za-z0-9\\-._~]{43,128}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // A handle is what the authorization-code generator produces: 32 random
    // bytes as 64 lowercase hex characters (OI-142). Anything else never reaches
    // the database. \z, not $: in .NET $ also matches before a final newline.
    private static readonly Regex HandlePattern = new(
        "^[0-9a-f]{64}\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private const int MaxStateLength = 512;

    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IIdpSessionRepository _idpSessionRepository;
    private readonly IAuthorizationCodeRepository _authorizationCodeRepository;
    private readonly IAuthorizationRequestRepository _authorizationRequestRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IRefreshTokenKeyService _refreshTokenKeyService;
    private readonly IStepUpTicketService _stepUpTicketService;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly OrganizationCreatorRoleCheck _creatorRoleCheck;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<AuthorizeCommandHandler> _logger;

    public AuthorizeCommandHandler(
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IIdpSessionRepository idpSessionRepository,
        IAuthorizationCodeRepository authorizationCodeRepository,
        IAuthorizationRequestRepository authorizationRequestRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        IRefreshTokenKeyService refreshTokenKeyService,
        IStepUpTicketService stepUpTicketService,
        IOrganizationRepository organizationRepository,
        OrganizationCreatorRoleCheck creatorRoleCheck,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        ILogger<AuthorizeCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _idpSessionRepository = idpSessionRepository;
        _authorizationCodeRepository = authorizationCodeRepository;
        _authorizationRequestRepository = authorizationRequestRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _refreshTokenKeyService = refreshTokenKeyService;
        _stepUpTicketService = stepUpTicketService;
        _organizationRepository = organizationRepository;
        _creatorRoleCheck = creatorRoleCheck;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    public Task<ErrorOr<AuthorizeResult>> Handle(AuthorizeCommand request, CancellationToken cancellationToken)
        => AuthorizeAsync(request, resumedHandle: null, cancellationToken);

    /// <summary>
    /// Resumes a stored request: the accounts page is done, and the browser comes
    /// back with the client id and the handle the redirect gave it.
    /// </summary>
    /// <remarks>
    /// An unknown, expired or malformed handle, or a client id that is not the
    /// stored request's, ends on the accounts page that says the request expired.
    /// Never a JSON error: this is a top-level navigation, and the person needs a
    /// page with a way back to the application.
    /// </remarks>
    public async Task<ErrorOr<AuthorizeResult>> Handle(
        ResumeAuthorizationCommand request, CancellationToken cancellationToken)
    {
        var stored = await FindLiveRequestAsync(request, cancellationToken);
        if (stored is null)
        {
            return await SignInExpiredAsync(request.ClientId, cancellationToken);
        }

        // "Continue without an organization": persisted, because the URL rewrite
        // it replaces was permanent for that flow. A later bounce through sign-in
        // must not bring the organization page back.
        if (request.SkipOrganization && stored.CreateOrganization)
        {
            stored.DropOrganizationRequest();
            await _authorizationRequestRepository.ClearCreateOrganizationAsync(stored.Id, cancellationToken);
        }

        return await AuthorizeAsync(FromStored(stored, request), request.Handle, cancellationToken);
    }

    /// <summary>
    /// The authorize pipeline, for a fresh request and for a resumed one alike.
    /// </summary>
    /// <param name="request">The request's values: as sent, or as stored.</param>
    /// <param name="resumedHandle">
    /// The handle of the stored request being resumed, or null for a fresh one.
    /// A resume that ends on an accounts page again carries the same handle.
    /// </param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    private async Task<ErrorOr<AuthorizeResult>> AuthorizeAsync(
        AuthorizeCommand request, string? resumedHandle, CancellationToken cancellationToken)
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

        // Before the session lookup, so a bad scope fails before any login page:
        // the user would otherwise sign in only to be refused on return. After the
        // client and redirect_uri checks, so the error only ever travels to a
        // validated redirect_uri.
        var scopeStatus = ScopeSet.TryParse(request.Scope, out var requestedScopes);
        if (scopeStatus != ScopeSet.ParseStatus.Parsed)
        {
            _logger.LogInformation(
                "Authorize request for client {ClientId} refused: scope parameter is {ScopeStatus}",
                request.ClientId, scopeStatus);

            return ErrorRedirect(request, "invalid_scope");
        }

        // create_organization accepts exactly "true": a value this server does
        // not understand is a malformed request, never a silent "no".
        if (request.CreateOrganization is not null &&
            !string.Equals(request.CreateOrganization, "true", StringComparison.Ordinal))
        {
            return ErrorRedirect(request, "invalid_request");
        }

        // Same placement as the scope check, for the same reason: an application
        // that cannot create organizations is told so before anyone signs up for
        // nothing. Re-checks the creator role every time; a role changed since
        // the administrator saved it makes the step unavailable, never wrong.
        Guid? creatorRoleId = null;
        if (request.CreateOrganization is not null)
        {
            creatorRoleId = await _creatorRoleCheck.GetAvailableCreatorRoleAsync(application, cancellationToken);
            if (creatorRoleId is null)
            {
                _logger.LogWarning(
                    "Authorize request for client {ClientId} asked to create an organization, which the application does not offer",
                    request.ClientId);

                return ErrorRedirect(request, "unauthorized_client");
            }
        }

        // Every check above has passed: this is what an accounts page may store.
        var pending = new PendingContext(application, prompt, requestedScopes, resumedHandle);

        // --- IdP session: no valid session means the browser goes to login ---

        var session = await ResolveSessionAsync(request.IdpSessionToken, cancellationToken);
        if (session is null)
        {
            // prompt=create (OIDC "Initiating User Registration"): with no
            // session, open the registration page instead of sign-in.
            return await LoginRequiredAsync(
                request, pending, demandStepUp: false, cancellationToken, offerRegistration: prompt.Create);
        }

        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.CanRenewCredentials())
        {
            // Force a fresh interactive login instead of leaking account state.
            return await LoginRequiredAsync(request, pending, demandStepUp: false, cancellationToken);
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

            return await LoginRequiredAsync(request, pending, demandStepUp: true, cancellationToken);
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

        // --- Organization: the application asked that the user have one ---
        // After entitlement, so the creation page is shown only to someone the
        // application admits. "Set up" is the repository's one definition, the
        // same one the creation step asserts before committing, so a user who
        // finished the step is never sent back to it.
        if (creatorRoleId is Guid roleId &&
            await _organizationRepository.FindOrganizationSetUpForApplicationAsync(
                user.Id, application.Id, roleId, cancellationToken) is null)
        {
            if (prompt.None)
            {
                return ErrorRedirect(request, "interaction_required");
            }

            _logger.LogInformation(
                "User {UserId} has no organization set up for client {ClientId}; sending them to create one",
                user.Id, request.ClientId);

            return new AuthorizeResult
            {
                RedirectUrl = await AccountsRedirectAsync("create-organization", request, pending, cancellationToken)
            };
        }

        // --- Grant: what was asked for that the application is allowed ---
        // A known scope the application is not allowed is dropped, never an
        // error (RFC 6749 §3.3): the sign-in works, and the token response says
        // what was actually granted.
        var grantedScopes = requestedScopes.Intersect(application.AllowedScopes);
        var droppedScopes = requestedScopes.Except(application.AllowedScopes);
        if (droppedScopes.Count > 0)
        {
            _logger.LogInformation(
                "Authorize request for client {ClientId} asked for scopes the application is not allowed; dropped {DroppedScopes}",
                request.ClientId, string.Join(' ', droppedScopes));
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
            request.IpAddress,
            grantedScopes);

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
    private async Task<AuthorizeResult> LoginRequiredAsync(
        AuthorizeCommand request,
        PendingContext pending,
        bool demandStepUp,
        CancellationToken cancellationToken,
        bool offerRegistration = false)
    {
        if (pending.Prompt.None)
        {
            return ErrorRedirect(request, "login_required");
        }

        return new AuthorizeResult
        {
            RedirectUrl = await AccountsRedirectAsync(
                offerRegistration ? "register" : "login", request, pending, cancellationToken),
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
        var create = false;

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
            else if (token.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                // OpenID Connect "Initiating User Registration" 1.0: show sign-up
                // rather than sign-in when there is no session. With a session the
                // account exists, so it changes nothing.
                create = true;
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

        values = new PromptValues(none, login, create);
        return true;
    }

    /// <summary>
    /// Which of the OIDC prompt values this request carries.
    /// </summary>
    private readonly record struct PromptValues(bool None, bool Login, bool Create)
    {
        /// <summary>
        /// The recognised values in a fixed order, as a stored request keeps them,
        /// or null for none. <see cref="TryParsePrompt"/> reads it back to the
        /// same values: the tokens it drops are the ones it ignores anyway.
        /// </summary>
        public string? Canonical
        {
            get
            {
                string?[] values = [None ? "none" : null, Login ? "login" : null, Create ? "create" : null];
                var present = values.OfType<string>().ToArray();
                return present.Length == 0 ? null : string.Join(' ', present);
            }
        }
    }

    /// <summary>
    /// A request that passed every check, with what a stored copy of it needs:
    /// its application, its parsed prompt and scopes, and the handle it already
    /// has when it is being resumed.
    /// </summary>
    private sealed record PendingContext(
        Auth.Domain.Entities.Application Application,
        PromptValues Prompt,
        ScopeSet RequestedScopes,
        string? ResumedHandle);

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

    /// <summary>
    /// An accounts page (<c>login</c>, <c>register</c> or
    /// <c>create-organization</c>) that sends the browser back here when it is
    /// done: <c>?client_id={code}&amp;auth_request={handle}</c>, with the request
    /// stored under the handle, or, with handles switched off, the full authorize
    /// URL as <c>returnTo</c>.
    /// </summary>
    /// <remarks>
    /// The handle URL holds no other parameter and no value with <c>://</c> in it:
    /// a web application firewall reads a parameter holding an off-site address
    /// as a remote file inclusion and refused the page (OI-145). The application
    /// code is the only thing the page shows from it, for branding; the resume
    /// refuses a code that is not the stored request's.
    /// </remarks>
    private async Task<string> AccountsRedirectAsync(
        string page, AuthorizeCommand request, PendingContext pending, CancellationToken cancellationToken)
    {
        var accountsBase = _idpSettings.AccountsBaseUrl.TrimEnd('/');

        // A resume that lands on an accounts page again keeps its handle: the
        // stored request is still the one being served, so no second row. Also
        // with the switch off, which only stops new handles being made.
        var handle = pending.ResumedHandle;
        if (handle is null)
        {
            if (!_idpSettings.AuthorizationRequestHandlesEnabled)
            {
                return $"{accountsBase}/{page}?returnTo={Uri.EscapeDataString(request.OriginalRequestUrl)}";
            }

            handle = await StoreRequestAsync(request, pending, cancellationToken);
        }

        return $"{accountsBase}/{page}?client_id={Uri.EscapeDataString(request.ClientId!)}&auth_request={handle}";
    }

    /// <summary>
    /// Stores the validated request under a new handle and returns the handle.
    /// Only its hash is stored, and it never appears in a log line.
    /// </summary>
    /// <remarks>
    /// A failed insert is not caught: the central handler reports it. Falling back
    /// to the full URL would hand the browser, silently, to the firewall rule the
    /// handle exists to avoid.
    /// </remarks>
    private async Task<string> StoreRequestAsync(
        AuthorizeCommand request, PendingContext pending, CancellationToken cancellationToken)
    {
        // The authorization code's generator: 32 random bytes as 64 lowercase hex
        // characters since OI-142, nothing a firewall can read as anything.
        var handle = _jwtTokenService.GenerateRefreshToken();

        var stored = AuthorizationRequest.Create(
            _refreshTokenKeyService.ComputeTokenHash(handle),
            pending.Application.Id,
            request.ClientId!,
            request.ResponseType!,
            request.RedirectUri!,
            request.CodeChallenge!,
            request.CodeChallengeMethod!,
            request.State,
            pending.Prompt.Canonical,
            TryParseMaxAgeSeconds(request.MaxAge, out var maxAgeSeconds) ? maxAgeSeconds : null,
            pending.RequestedScopes.Value,
            // Validated above: present means "true".
            createOrganization: request.CreateOrganization is not null,
            _idpSettings.AuthorizationRequestLifetime,
            DateTime.UtcNow);

        await _authorizationRequestRepository.CreateAsync(stored, cancellationToken);

        _logger.LogInformation(
            "Stored pending authorize request {AuthorizationRequestId} for client {ClientId}",
            stored.Id, request.ClientId);

        return handle;
    }

    /// <summary>
    /// The stored request a resume names, when the handle is well formed, live,
    /// and was issued to the client id the page carried; otherwise null.
    /// </summary>
    private async Task<AuthorizationRequest?> FindLiveRequestAsync(
        ResumeAuthorizationCommand request, CancellationToken cancellationToken)
    {
        if (request.Handle is null || !HandlePattern.IsMatch(request.Handle))
        {
            _logger.LogInformation(
                "Authorize resume for client {ClientId} refused: the handle is malformed", request.ClientId);
            return null;
        }

        var now = DateTime.UtcNow;
        var stored = await _authorizationRequestRepository.GetLiveByHandleHashAsync(
            _refreshTokenKeyService.ComputeTokenHash(request.Handle), now, cancellationToken);

        if (stored is null || !stored.IsLive(now))
        {
            _logger.LogInformation(
                "Authorize resume for client {ClientId} refused: no live request under the handle", request.ClientId);
            return null;
        }

        // The page shows the client the URL names, so a client id edited in the
        // address bar must not resume another client's request.
        if (!stored.IsFor(request.ClientId))
        {
            _logger.LogWarning(
                "Authorize resume refused: client {ClientId} is not the client of pending request {AuthorizationRequestId}",
                request.ClientId, stored.Id);
            return null;
        }

        return stored;
    }

    /// <summary>
    /// The accounts page for a request that expired or is no longer valid. It
    /// names the application only when the client id is an active
    /// application's, and then in its stored form: nothing else from the
    /// request goes back out.
    /// </summary>
    private async Task<AuthorizeResult> SignInExpiredAsync(string? clientId, CancellationToken cancellationToken)
    {
        var page = $"{_idpSettings.AccountsBaseUrl.TrimEnd('/')}/sign-in-expired";

        var application = string.IsNullOrWhiteSpace(clientId)
            ? null
            : await _applicationRepository.GetByCodeAsync(clientId, cancellationToken);

        return new AuthorizeResult
        {
            RedirectUrl = application is { IsActive: true }
                ? $"{page}?client_id={Uri.EscapeDataString(application.Code)}"
                : page
        };
    }

    /// <summary>
    /// The stored request as the pipeline reads a fresh one. Every value goes
    /// through the same checks again; the cookies and the address are this
    /// browser's, now.
    /// </summary>
    private static AuthorizeCommand FromStored(AuthorizationRequest stored, ResumeAuthorizationCommand resume) => new(
        stored.ResponseType,
        stored.ClientId,
        stored.RedirectUri,
        stored.CodeChallenge,
        stored.CodeChallengeMethod,
        stored.State,
        resume.IdpSessionToken,
        // Never read on a resume: an accounts redirect reuses the handle.
        OriginalRequestUrl: string.Empty,
        resume.IpAddress,
        stored.Prompt,
        stored.MaxAge?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        resume.StepUpTicket,
        stored.Scope,
        stored.CreateOrganization ? "true" : null);

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
