using System.Text.RegularExpressions;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth.Domain.Enums;

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

    private const int MaxStateLength = RelyingPartyRedirect.MaxStateLength;

    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _applicationAccessRepository;
    private readonly IIdpSessionReader _idpSessionReader;
    private readonly IAuthorizationCodeRepository _authorizationCodeRepository;
    private readonly IAuthorizationRequestRepository _authorizationRequestRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IStepUpTicketService _stepUpTicketService;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly OrganizationCreatorRoleCheck _creatorRoleCheck;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthorizeCommandHandler> _logger;

    public AuthorizeCommandHandler(
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository applicationAccessRepository,
        IIdpSessionReader idpSessionReader,
        IAuthorizationCodeRepository authorizationCodeRepository,
        IAuthorizationRequestRepository authorizationRequestRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        ICredentialHasher credentialHasher,
        IStepUpTicketService stepUpTicketService,
        IOrganizationRepository organizationRepository,
        OrganizationCreatorRoleCheck creatorRoleCheck,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        TimeProvider timeProvider,
        ILogger<AuthorizeCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _applicationAccessRepository = applicationAccessRepository;
        _idpSessionReader = idpSessionReader;
        _authorizationCodeRepository = authorizationCodeRepository;
        _authorizationRequestRepository = authorizationRequestRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _credentialHasher = credentialHasher;
        _stepUpTicketService = stepUpTicketService;
        _organizationRepository = organizationRepository;
        _creatorRoleCheck = creatorRoleCheck;
        _idpSettings = idpSettings.Value;
        _timeProvider = timeProvider;
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

        // Either list admits the address (OI-171): the public one, or the server-side one, whose
        // code only a request authenticated with the application's sign-in key may redeem. An
        // address is never in both (Application.RedirectUriInBothLists at save).
        var confidentialRedirect = !string.IsNullOrWhiteSpace(request.RedirectUri) &&
                                   application.IsConfidentialRedirectUriAllowed(request.RedirectUri);
        if (string.IsNullOrWhiteSpace(request.RedirectUri) ||
            (!confidentialRedirect && !application.IsRedirectUriAllowed(request.RedirectUri)))
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

        if (!PromptValues.TryParse(request.Prompt, out var prompt))
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
        // The presentation hints are no check: an unusable value is dropped, never
        // refused, so they cannot turn a valid request into an error.
        var pending = new PendingContext(
            application,
            prompt,
            requestedScopes,
            resumedHandle,
            PresentationHints.From(request.UiLocales, request.UiTheme));

        // --- IdP session: no valid session means the browser goes to login ---

        // On each of the three ways to an accounts page below, prompt=login records
        // its demand on the way out (OI-150). The sign-in it leads to then answers
        // it, and the request comes back to a code: one sign-in, not two. Without
        // the ticket, the return found no proof and sent the person to sign in a
        // second time. And prompt=create opens registration wherever the person
        // has to authenticate, with the sign-in link one click away there.

        // Valid means not revoked, not expired and, for a browser session, not idle
        // (OI-154). The use is recorded below, when a code signs the person in.
        var session = await _idpSessionReader.FindLiveAsync(
            request.IdpSessionToken, IdpSessionReadMode.Check, cancellationToken);
        if (session is null)
        {
            // prompt=create (OIDC "Initiating User Registration"): with no
            // session, open the registration page instead of sign-in.
            return await LoginRequiredAsync(
                request, pending, demandStepUp: prompt.Login, cancellationToken, offerRegistration: prompt.Create);
        }

        var user = await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.CanRenewCredentials())
        {
            // Force a fresh interactive login instead of leaking account state.
            return await LoginRequiredAsync(
                request, pending, demandStepUp: prompt.Login, cancellationToken, offerRegistration: prompt.Create);
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

            // With create, the registration page: a client that sends create with
            // login asks who is at the keyboard and offers sign-up. The page keeps
            // its sign-in link with the same request, for an existing account.
            return await LoginRequiredAsync(
                request, pending, demandStepUp: true, cancellationToken, offerRegistration: prompt.Create);
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

        // --- The session signs the person in: that is its use (OI-154) ---
        // Here, not at the read, so a refusal above (step-up, entitlement, the
        // organization step) keeps no session alive. The write re-checks that the
        // session still lives; one that ended meanwhile asks for the sign-in.
        if (!await _idpSessionReader.RecordUseAsync(session.Id, cancellationToken))
        {
            return await LoginRequiredAsync(
                request, pending, demandStepUp: prompt.Login, cancellationToken, offerRegistration: prompt.Create);
        }

        // --- Issue the one-time code bound to this exact request ---

        var plainCode = _jwtTokenService.GenerateRefreshToken();
        var code = AuthorizationCode.Create(
            application.Id,
            user.Id,
            _credentialHasher.Hash(CredentialPurpose.AuthorizationCode, plainCode),
            request.RedirectUri,
            request.CodeChallenge,
            _idpSettings.AuthorizationCodeLifetime,
            request.IpAddress,
            grantedScopes,
            // Carried to the application's refresh tokens, whose use then counts
            // as this session's (OI-154, D-154-2 (b)).
            idpSessionId: session.Id,
            // Issued to a server-side redirect URI: redeemable only with the
            // application's sign-in key (OI-171).
            requiresClientAuthentication: confidentialRedirect,
            // The browser that asked, as seen now: a server-side application's
            // exchange records the person from these, not from its own request.
            // The device id is the sign-in's: a navigation sends none (OI-178).
            // Without the header the code stores an empty user agent, never NULL
            // (AuthorizationCode.Create): NULL marks a code written before the
            // column, and leaving the header out must not buy that.
            userAgent: request.UserAgent,
            deviceId: session.DeviceId);

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
    /// demand is being raised. The ticket is bound to this request's
    /// <c>code_challenge</c>, so only this request can spend it.
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
                ? _stepUpTicketService.Issue(request.ClientId!, request.CodeChallenge!, UtcNow())
                : null
        };
    }

    /// <summary>
    /// A request that passed every check, with what a stored copy of it needs:
    /// its application, its parsed prompt and scopes, the handle it already has
    /// when it is being resumed, and how its accounts pages should look.
    /// </summary>
    private sealed record PendingContext(
        Auth.Domain.Entities.Application Application,
        PromptValues Prompt,
        ScopeSet RequestedScopes,
        string? ResumedHandle,
        PresentationHints Hints);

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
    /// Two limits on that evidence (OI-150). The ticket answers only the request
    /// that raised it: it is signed over that request's <c>code_challenge</c>, so
    /// a later request of the same client on the same browser finds no proof and
    /// demands again. And a proof is good only within the finish window after the
    /// sign-in (<see cref="IdentityProviderSettings.StepUpFinishWindow"/>): the
    /// steps that follow it, the organization page among them, must be finished in
    /// that time, or the person signs in again. A page left open, or brought back
    /// with the Back button, is then no longer enough to finish someone else's
    /// sign-in.
    /// </para>
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
        var now = UtcNow();
        var ticketSpent = false;

        if (prompt.Login)
        {
            var proven =
                _stepUpTicketService.TryValidate(
                    request.StepUpTicket,
                    request.ClientId!,
                    request.CodeChallenge,
                    now,
                    _idpSettings.StepUpTicketLifetime,
                    out var demandedAtUtc)
                && session.CreatedAt >= demandedAtUtc
                && _idpSettings.IsWithinStepUpFinishWindow(session.CreatedAt, now);

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
    /// as a remote file inclusion and refuses the page (OI-145). The application
    /// code is the only thing the page shows from it, for branding; the resume
    /// refuses a code that is not the stored request's.
    /// <para>
    /// The presentation hints ride along in either shape, resolved: a language
    /// code and a colour mode, short words a firewall reads as nothing. The page
    /// keeps them for the rest of the flow and never sends them back; a resume
    /// reads them from the stored request.
    /// </para>
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
                return PresentationHintsQuery.AppendTo(
                    $"{accountsBase}/{page}?returnTo={Uri.EscapeDataString(request.OriginalRequestUrl)}",
                    pending.Hints);
            }

            handle = await StoreRequestAsync(request, pending, cancellationToken);
        }

        return PresentationHintsQuery.AppendTo(
            $"{accountsBase}/{page}?client_id={Uri.EscapeDataString(request.ClientId!)}&auth_request={handle}",
            pending.Hints);
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
            _credentialHasher.Hash(CredentialPurpose.AuthorizationRequestHandle, handle),
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
            UtcNow(),
            pending.Hints);

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
        if (!AuthorizationRequestHandle.IsWellFormed(request.Handle))
        {
            _logger.LogInformation(
                "Authorize resume for client {ClientId} refused: the handle is malformed", request.ClientId);
            return null;
        }

        var now = UtcNow();
        var stored = await _authorizationRequestRepository.GetLiveByHandleHashAsync(
            _credentialHasher.Candidates(CredentialPurpose.AuthorizationRequestHandle, request.Handle), now, cancellationToken);

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
    /// <remarks>Internal for the round-trip test over every authorize parameter.</remarks>
    internal static AuthorizeCommand FromStored(AuthorizationRequest stored, ResumeAuthorizationCommand resume) => new(
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
        stored.CreateOrganization ? "true" : null,
        resume.UserAgent,
        // Resolved values, which resolve to themselves: the resume URL never
        // carries the hints, so the stored request is their only way back.
        stored.Hints.Language,
        stored.Hints.Theme);

    /// <summary>The current time, from the injected clock, as every rule here reads it.</summary>
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static AuthorizeResult ErrorRedirect(AuthorizeCommand request, string oauthError)
    {
        return new AuthorizeResult
        {
            RedirectUrl = AppendQuery(request.RedirectUri!, "error", oauthError, request.State)
        };
    }

    private static string AppendQuery(string redirectUri, string key, string value, string? state)
    {
        var url = RelyingPartyRedirect.AppendQuery(redirectUri, key, value);

        return string.IsNullOrEmpty(state)
            ? url
            : RelyingPartyRedirect.AppendQuery(url, "state", state);
    }
}
