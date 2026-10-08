using System.Security.Claims;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Features.Authentication.ChangePassword;
using Auth.Application.Features.Authentication.CompleteRegistration;
using Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;
using Auth.Application.Features.AccountDeletion.PublicRequestDeletion;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.Authentication.ForgetKnownDevice;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Features.Authentication.GetKnownDevices;
using Auth.Application.Features.Authentication.GetLoginHistory;
using Auth.Application.Features.Authentication.GetOidcUserInfo;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.Logout;
using Auth.Application.Features.Authentication.LogoutWithRefreshCookie;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.ResendEmailVerification;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Application.Features.Authentication.SendEmailVerification;
using Auth.Application.Features.Authentication.StartRegistration;
using Auth.Application.Features.Authentication.TerminateAllSessions;
using Auth.Application.Features.Authentication.TerminateSession;
using Auth.Application.Features.Authentication.VerifyEmail;
using Auth.Application.Features.Authentication.VerifyRegistration;
using Auth_API.Modules.Authentication.Contracts;
using Auth.Application.Features.Authentication.GetUserSessions;
using Auth.Application.Features.Organizations.OrganizationSetup;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Shared.Http.ErrorContract;
using MediatR;
using Auth_API.Common;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.FirstParty;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// Authentication endpoints for login, logout, and token management.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
public class AuthController : ApiController
{
    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ISender sender,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        ILogger<AuthController> logger)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a user with email and password.
    /// </summary>
    /// <param name="request">Login credentials</param>
    /// <returns>JWT tokens and user information</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(
            request.Email,
            request.Password,
            GetClientIpAddress(),
            GetUserAgent(),
            GetDeviceId(request.DeviceId));

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Step 1 of verify-first self-registration: takes an email address, mails
    /// a code to it, and creates nothing. Answers the same shape for every
    /// address, whether it is free, already an account, or reserved.
    /// </summary>
    /// <param name="request">The address and, optionally, the site language.</param>
    /// <returns>The opaque handle for the next steps, the masked address, and when the code expires.</returns>
    [HttpPost("registration/start")]
    [AllowAnonymous]
    // The one request of a sign-up that produces a message, so it stays on the
    // sign-up budget: one "register" permit per registration, as before.
    [EnableRateLimiting("register")]
    // 200, not 201: nothing was created.
    [ProducesResponseType(typeof(StartRegistrationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    // Registration:AllowSelfRegistration closed — User.SelfRegistrationClosed.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> StartRegistration([FromBody] StartRegistrationRequest request, CancellationToken cancellationToken)
    {
        var command = new StartRegistrationCommand(
            request.Email,
            request.PreferredLanguage,
            GetClientIpAddress(),
            GetUserAgent());

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Step 2 of verify-first self-registration: checks the code against the
    /// pending row and consumes nothing. The same code is presented again at
    /// completion, which is the step that consumes it.
    /// </summary>
    /// <param name="request">The handle from step 1 and the six-digit code.</param>
    [HttpPost("registration/verify")]
    [AllowAnonymous]
    // The follow-up budget, not the sign-up one: this step sends no message, and
    // every sign-up makes two of these (a check and a completion) per start.
    [EnableRateLimiting("registration-followup")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // EmailVerification.InvalidOtpFormat | InvalidOrExpiredOtp | TooManyAttempts
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyRegistration([FromBody] VerifyRegistrationRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new VerifyRegistrationCommand(request.PendingId, request.Otp), cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Step 3 of verify-first self-registration: creates the account for the
    /// address the code proved, consumes the code, and signs the new owner in.
    /// </summary>
    /// <param name="request">The handle, the code once more, the password and the name.</param>
    /// <returns>The same session a sign-in issues.</returns>
    [HttpPost("registration/complete")]
    [AllowAnonymous]
    [EnableRateLimiting("registration-followup")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    // Password policy, or EmailVerification.InvalidOtpFormat | InvalidOrExpiredOtp | TooManyAttempts.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    // Registration:AllowSelfRegistration closed — User.SelfRegistrationClosed.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    // User.DuplicateEmail: another door created the account first, or the address is reserved.
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CompleteRegistration([FromBody] CompleteRegistrationRequest request, CancellationToken cancellationToken)
    {
        var command = new CompleteRegistrationCommand(
            request.PendingId,
            request.Otp,
            request.Password,
            request.FirstName,
            request.LastName,
            request.TimeZone,
            request.CreateOrganization,
            GetDeviceId(request.DeviceId),
            GetClientIpAddress(),
            GetUserAgent());

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Returns enabled external authentication providers for UI rendering.
    /// </summary>
    /// <returns>List of enabled providers with code, name, and icon URL</returns>
    [HttpGet("external-providers")]
    [AllowAnonymous]
    // Page rendering, not authentication. Carried no limiter at all before, which
    // was its own gap: the gateway's catch-all was the only thing counting it, and
    // it counted it as a sign-in attempt.
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(typeof(IReadOnlyList<ExternalAuthProviderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetExternalProviders(
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetExternalProvidersQuery(sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match<IActionResult>(
            providers => Ok(providers),
            errors => Problem(errors));
    }

    /// <summary>
    /// Issues a single-use nonce for a provider sign-in that is about to start.
    /// </summary>
    /// <remarks>
    /// Called immediately before the provider's SDK is initialised. The plain
    /// value goes to the provider, which seals it into the signed ID token; the
    /// matching hash is stored in an HttpOnly cookie here. The sign-in then has to
    /// present a value this server issued to this browser, which a replayed token
    /// minted for someone else's browser cannot do.
    /// <para>
    /// POST, not GET: it hands out a fresh value and writes a cookie every time,
    /// and no cache anywhere may serve one browser's nonce to another.
    /// </para>
    /// </remarks>
    /// <returns>The plain nonce for the caller to pass to the provider.</returns>
    [HttpPost("external-nonce")]
    [AllowAnonymous]
    // Fetched on mount by every sign-in and sign-up page that offers Google, so it
    // is page rendering rather than a sign-in attempt. The work is a random value
    // and one keyed hash, with no database touched.
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(typeof(ExternalNonceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> IssueExternalNonce(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new IssueExternalNonceCommand(), cancellationToken);

        return result.Match<IActionResult>(
            issued =>
            {
                ExternalNonceCookie.Apply(Response, issued.CookieValue);
                Response.Headers.CacheControl = "no-store";
                return Ok(new ExternalNonceResponse(issued.Nonce));
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Authenticates a user via an external provider (e.g., Google).
    /// Creates a new account if the user doesn't exist, or logs in if they do.
    /// </summary>
    /// <param name="request">External provider token and details</param>
    /// <returns>JWT tokens and user information</returns>
    [HttpPost("external-login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request, CancellationToken cancellationToken)
    {
        var command = new ExternalLoginCommand(
            request.Provider,
            request.IdToken,
            request.Nonce,
            request.CreateOrganization,
            GetClientIpAddress(),
            GetUserAgent(),
            DeviceId: GetDeviceId(request.DeviceId),
            AuthorizationCode: request.AuthorizationCode,
            GivenName: request.GivenName,
            FamilyName: request.FamilyName,
            NonceCookie: ExternalNonceCookie.Read(Request));

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Refreshes an access token using a valid refresh token.
    /// </summary>
    /// <remarks>
    /// The token is read from the body when the body carries one — every non-browser
    /// client, and a session an older app bundle stored — and otherwise from the
    /// refresh cookie of the first-party app the Origin names. The platform's own
    /// apps send <c>{}</c>: their token lives in an HttpOnly cookie no script reads.
    /// </remarks>
    /// <param name="request">Refresh token, optional for the first-party apps.</param>
    /// <param name="credentialReader">Reads the token from the body or the cookie.</param>
    /// <returns>New JWT tokens</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        [FromServices] RefreshCredentialReader credentialReader,
        CancellationToken cancellationToken)
    {
        if (credentialReader.Read(HttpContext, request.RefreshToken) is not { } credential)
        {
            return Problem([Auth.Domain.Errors.AuthErrors.RefreshTokenNotFound]);
        }

        var command = new RefreshTokenCommand(
            credential.Value,
            GetClientIpAddress(),
            GetUserAgent(),
            // The grace window exists for the cookie delivery. With the switch off
            // the cookie is still read (the rollback path) but earns no grace: a
            // non-browser client can forge a listed Origin and the cookie header.
            ReplayGraceEligible: credential.Channel == RefreshCredentialChannel.Cookie
                && _idpSettings.SpaRefreshCookieEnabled);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// OAuth 2.0 authorization endpoint (authorization-code + PKCE).
    /// With a valid IdP session, 302s back to the registered redirect_uri with
    /// a one-time code; without one, 302s to the accounts login page. Unknown
    /// client_id or unregistered redirect_uri returns 400 without redirecting.
    /// </summary>
    [HttpGet("authorize")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "response_type")] string? responseType,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "prompt")] string? prompt,
        [FromQuery(Name = "max_age")] string? maxAge,
        [FromQuery(Name = "scope")] string? scope,
        [FromQuery(Name = "create_organization")] string? createOrganization,
        CancellationToken cancellationToken)
    {
        // Rebuild the authorize URL from the CONFIGURED public origin, not from
        // Request.Host: behind the gateway the host is the internal destination
        // (e.g. identity.example.com), and the accounts app rejects a returnTo whose
        // origin is not the public auth origin — which would break cold-start
        // SSO. Falls back to the request host only where no proxy exists (dev).
        var publicBaseUrl = _idpSettings.ResolvePublicBaseUrl($"{Request.Scheme}://{Request.Host}");
        var originalRequestUrl = $"{publicBaseUrl}{Request.Path}{Request.QueryString}";

        var command = new AuthorizeCommand(
            responseType,
            clientId,
            redirectUri,
            codeChallenge,
            codeChallengeMethod,
            state,
            IdpSessionCookie.Read(Request, _idpSettings),
            originalRequestUrl,
            GetClientIpAddress(),
            prompt,
            maxAge,
            StepUpCookie.Read(Request, _idpSettings),
            scope,
            // Model binding turns "create_organization=" into null, which would
            // read as "not asked". Present but empty is a value, and not "true".
            createOrganization ?? (Request.Query.ContainsKey("create_organization") ? string.Empty : null));

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response =>
            {
                // Cookie writes belong to this layer; the handler only decides.
                if (response.StepUpTicketToSet is { } ticket)
                {
                    StepUpCookie.Apply(Response, ticket, _idpSettings);
                }
                else if (response.ClearStepUpTicket)
                {
                    StepUpCookie.Delete(Response, _idpSettings);
                }

                return Redirect(response.RedirectUrl);
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// OAuth 2.0 token endpoint (RFC 6749 §3.2, form-encoded). Supports the
    /// authorization_code grant (PKCE mandatory, public clients — no client
    /// secret) and the refresh_token grant.
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(OAuthTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Token([FromForm] OAuthTokenRequest request, CancellationToken cancellationToken)
    {
        switch (request.GrantType)
        {
            case "authorization_code":
            {
                var command = new ExchangeAuthorizationCodeCommand(
                    request.Code,
                    request.RedirectUri,
                    request.ClientId,
                    request.CodeVerifier,
                    GetClientIpAddress(),
                    GetUserAgent(),
                    GetDeviceId());

                var result = await _sender.Send(command, cancellationToken);

                return result.Match<IActionResult>(
                    response => Ok(response),
                    errors => Problem(errors));
            }

            case "refresh_token":
            {
                var command = new RefreshTokenCommand(
                    request.RefreshToken ?? string.Empty,
                    GetClientIpAddress(),
                    GetUserAgent());

                var result = await _sender.Send(command, cancellationToken);

                return result.Match<IActionResult>(
                    response => Ok(new OAuthTokenResponse
                    {
                        AccessToken = response.AccessToken,
                        ExpiresIn = response.ExpiresIn,
                        RefreshToken = response.RefreshToken,
                        RefreshExpiresIn = response.RefreshExpiresIn,
                        Scope = response.Scope
                    }),
                    errors => Problem(errors));
            }

            default:
                return Problem([Auth.Domain.Errors.AuthErrors.UnsupportedGrantType]);
        }
    }

    /// <summary>
    /// OIDC RP-Initiated Logout: where a relying party sends the browser to end
    /// the single sign-on session it cannot reach itself.
    /// </summary>
    /// <remarks>
    /// A GET, because the specification defines this as a navigation rather than
    /// an API call — the browser arrives carrying the SSO cookie and no bearer
    /// token. It does not sign anyone out on its own: with no id_token_hint to
    /// prove who is asking (this provider issues no id tokens), acting here would
    /// let any page sign our users out by loading this URL in an image tag. It
    /// decides where to send the browser, and the user answers on the next screen.
    /// </remarks>
    [HttpGet("end-session")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EndSession(
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "state")] string? state,
        CancellationToken cancellationToken)
    {
        var command = new EndSessionCommand(
            clientId, state, IdpSessionCookie.Read(Request, _idpSettings));

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Redirect(response.RedirectUrl),
            errors => Problem(errors));
    }

    /// <summary>
    /// Ends the single sign-on session after the user confirmed it on the
    /// logout screen.
    /// </summary>
    /// <remarks>
    /// Anonymous by necessity, not by oversight: the browser was steered here by
    /// another site and carries no bearer token. The SSO cookie is the credential,
    /// and its SameSite=Lax setting is what stops a cross-site page from forging
    /// this call — Lax withholds the cookie from cross-site POSTs, so a forged
    /// request arrives with nothing to act on.
    /// </remarks>
    [HttpPost("end-session")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEndSession(CancellationToken cancellationToken)
    {
        var command = new ConfirmEndSessionCommand(IdpSessionCookie.Read(Request, _idpSettings));
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ =>
            {
                IdpSessionCookie.Delete(Response, _idpSettings);
                return NoContent();
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// What the organization-creation page may offer the signed-in user for an
    /// application: <c>canCreate</c>, the per-user <c>limit</c>, and the
    /// <c>ownedOrganizations</c> that could be set up instead.
    /// </summary>
    /// <remarks>
    /// Authenticated by the single sign-on cookie, as the sign-out confirmation
    /// is, so it answers for the user the authorize endpoint sees; a bearer token
    /// alone authenticates nothing here. No usable session is 401.
    /// </remarks>
    [HttpGet("organization-setup")]
    [AllowAnonymous]
    [EnableRateLimiting("sign-in-page")]
    [RequireFirstPartyOrigin]
    [ProducesResponseType(typeof(OrganizationSetupState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOrganizationSetup(
        [FromQuery(Name = "clientId")] string? clientId,
        CancellationToken cancellationToken)
    {
        var query = new GetOrganizationSetupQuery(IdpSessionCookie.Read(Request, _idpSettings), clientId);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match<IActionResult>(
            response => response.SignInRequired ? Unauthorized() : Ok(response.State),
            errors => Problem(errors));
    }

    /// <summary>
    /// The organization-creation step: creates the signed-in user's organization
    /// (or sets up one they own), enables the application for it and grants the
    /// application's creator role there, in one transaction. Repeating it for an
    /// organization already set up changes nothing.
    /// </summary>
    /// <remarks>
    /// The cookie is the credential, so the first-party Origin barrier guards it
    /// against a page on a sibling site, and its SameSite=Lax setting against a
    /// cross-site one, as for the sign-out confirmation.
    /// </remarks>
    [HttpPost("organization-setup")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [ProducesResponseType(typeof(SetUpOrganizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetUpOrganization(
        [FromBody] SetUpOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SetUpOrganizationCommand(
            IdpSessionCookie.Read(Request, _idpSettings),
            request.ClientId,
            request.Name,
            request.OrganizationId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => response.SignInRequired
                ? Unauthorized()
                : Ok(new SetUpOrganizationResponse(response.OrganizationId!.Value)),
            errors => Problem(errors));
    }

    /// <summary>
    /// Logs out the current user and revokes their tokens.
    /// </summary>
    /// <param name="request">Logout options</param>
    /// <returns>Success status</returns>
    [HttpPost("logout")]
    [Authorize]
    [ClearsFirstPartySession]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new LogoutCommand(
            userId,
            request?.RefreshToken,
            GetAccessToken(),
            GetClientIpAddress(),
            request?.LogoutAllDevices ?? false,
            GetCurrentSessionId(),
            IdpSessionCookie.Read(Request, _idpSettings));

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ =>
            {
                IdpSessionCookie.Delete(Response, _idpSettings);
                return NoContent();
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Signs this browser out with the first-party refresh cookie alone, no bearer.
    /// </summary>
    /// <remarks>
    /// The bearer sign-out above answers 401 before anything runs when the access
    /// token has expired or was refused - an idle tab whose last refresh failed -
    /// and the HttpOnly cookie would then outlive a sign-out the screen reports
    /// as done. This ends the cookie's session through the same revocation, and
    /// deletes the cookie. Only the app the Origin names can use it (its cookie is
    /// the only one read), and SameSite=Strict keeps it off every cross-site
    /// request. When <c>sessionId</c> is given and the cookie now belongs to another
    /// session, nothing is ended: a new sign-in happened since.
    /// </remarks>
    [HttpPost("logout/cookie")]
    [AllowAnonymous]
    [RequireFirstPartyOrigin]
    [ProducesResponseType(typeof(LogoutWithRefreshCookieResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LogoutWithRefreshCookie(
        [FromBody] LogoutWithRefreshCookieRequest? request,
        [FromServices] IFirstPartyOriginResolver originResolver,
        CancellationToken cancellationToken)
    {
        if (originResolver.Resolve(Request) is not { } app)
        {
            return Problem([Auth.Domain.Errors.AuthErrors.FirstPartyOriginRequired]);
        }

        if (FirstPartyRefreshCookie.Read(Request, app) is not { } refreshToken)
        {
            return Ok(new LogoutWithRefreshCookieResult(Ended: true));
        }

        var result = await _sender.Send(
            new LogoutWithRefreshCookieCommand(
                refreshToken, request?.SessionId, IdpSessionCookie.Read(Request, _idpSettings)),
            cancellationToken);

        return result.Match<IActionResult>(
            outcome =>
            {
                if (outcome.Ended)
                {
                    FirstPartyRefreshCookie.Delete(Response, app);
                    IdpSessionCookie.Delete(Response, _idpSettings);
                }

                return Ok(outcome);
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Changes the current user's password.
    /// </summary>
    /// <param name="request">Password change request with current and new passwords.</param>
    /// <returns>Success status</returns>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new ChangePasswordCommand(
            userId,
            request.CurrentPassword,
            request.NewPassword,
            request.TerminateSessions,
            GetCurrentSessionId(),
            IdpSessionCookie.Read(Request, _idpSettings),
            request.ConfirmNewPassword);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Initiates a password reset flow by generating a reset token.
    /// </summary>
    /// <param name="request">Email address for password reset.</param>
    /// <returns>Reset token information (in production, sent via email).</returns>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ForgotPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(request.Email);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Resets a user's password using a reset token.
    /// </summary>
    /// <param name="request">Reset token and new password.</param>
    /// <returns>Success status</returns>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("password-reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(
            request.Token,
            request.NewPassword,
            request.TerminateSessions,
            request.ConfirmNewPassword);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Gets the current user's active sessions.
    /// </summary>
    /// <returns>List of active sessions</returns>
    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<SessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSessions(
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var query = new GetUserSessionsQuery(userId, GetCurrentSessionId(), sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match<IActionResult>(
            sessions => Ok(sessions),
            errors => Problem(errors));
    }

    /// <summary>
    /// Terminates a specific session.
    /// </summary>
    /// <param name="sessionId">The ID of the session to terminate.</param>
    /// <returns>Success status</returns>
    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TerminateSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new TerminateSessionCommand(userId, sessionId);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Terminates all sessions except the current one.
    /// </summary>
    /// <returns>Number of sessions terminated</returns>
    [HttpDelete("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(TerminatedCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> TerminateAllSessions(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        // Exclude the current session and, if this browser holds one, its SSO
        // session — signing out everywhere should not sign out here.
        var command = new TerminateAllSessionsCommand(
            userId,
            GetCurrentSessionId(),
            IdpSessionCookie.Read(Request, _idpSettings));
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            count => Ok(new TerminatedCountResponse(count)),
            errors => Problem(errors));
    }

    /// <summary>
    /// Gets the browsers the current user has signed in from.
    /// </summary>
    /// <returns>List of known devices</returns>
    [HttpGet("devices")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<KnownDeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetKnownDevices(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var query = new GetKnownDevicesQuery(userId, GetCurrentSessionId());
        var result = await _sender.Send(query, cancellationToken);

        return result.Match<IActionResult>(
            devices => Ok(devices),
            errors => Problem(errors));
    }

    /// <summary>
    /// Forgets a browser: removes its recognition record and ends every session
    /// it still holds. The browser carrying the current session cannot be
    /// forgotten — signing out is what ends that one.
    /// </summary>
    /// <param name="deviceId">The ID of the device to forget.</param>
    /// <returns>Number of sessions ended</returns>
    [HttpDelete("devices/{deviceId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(TerminatedCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForgetKnownDevice(Guid deviceId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new ForgetKnownDeviceCommand(userId, deviceId, GetCurrentSessionId());
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            count => Ok(new TerminatedCountResponse(count)),
            errors => Problem(errors));
    }

    /// <summary>
    /// Gets the current user's recent sign-in attempts, successful and failed.
    /// Read-only: this is the record of what happened, not a set of live
    /// credentials, so nothing here can be revoked.
    /// </summary>
    /// <param name="take">How many entries to return (1-100).</param>
    /// <returns>List of recent login attempts</returns>
    [HttpGet("login-history")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<LoginAttemptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetLoginHistory(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var query = new GetLoginHistoryQuery(userId, take);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match<IActionResult>(
            attempts => Ok(attempts),
            errors => Problem(errors));
    }

    /// <summary>
    /// Gets the current authenticated user's information.
    /// </summary>
    /// <returns>User information</returns>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public IActionResult GetCurrentUser()
    {
        var userInfo = new UserInfo
        {
            Id = GetCurrentUserId(),
            Email = User.FindFirstValue(JwtClaimNames.Email) ?? string.Empty,
            FirstName = User.FindFirstValue(JwtClaimNames.GivenName) ?? string.Empty,
            LastName = User.FindFirstValue(JwtClaimNames.FamilyName) ?? string.Empty,
            DisplayName = User.FindFirstValue(JwtClaimNames.Name),
            PreferredLanguage = User.FindFirstValue(JwtClaimNames.Locale),
            TimeZone = User.FindFirstValue(JwtClaimNames.TimeZone),
            Theme = User.FindFirstValue(JwtClaimNames.Theme),
            Roles = User.FindAll(JwtClaimNames.Roles).Select(c => c.Value).ToList(),
            Permissions = User.FindAll(JwtClaimNames.Permissions).Select(c => c.Value).ToList(),
            // The token's mfa_req, echoed: present only while platform authority is
            // withheld, so its absence is "none".
            MfaRequirement = User.FindFirstValue(JwtClaimNames.MfaRequirement) ?? MfaRequirementValues.None
        };

        return Ok(userInfo);
    }

    /// <summary>
    /// The OpenID Connect UserInfo endpoint (OIDC Core §5.3), for applications.
    /// </summary>
    /// <remarks>
    /// Accepts an application's access token, and only here: the scheme it names takes exactly
    /// one application audience, which every other action refuses. The answer is the user's
    /// profile as it is now, limited to the scopes the token was granted. The token is read from
    /// the Authorization header only; a form field or query parameter named access_token is
    /// ignored. A subject that can no longer be served (deleted, deactivated, locked) is refused
    /// exactly as a revoked token is, so the caller learns nothing about why.
    /// </remarks>
    /// <returns>The standard claims the token's scopes allow.</returns>
    [HttpGet("userinfo")]
    [HttpPost("userinfo")]
    [Authorize(AuthenticationSchemes = AccessTokenValidation.UserInfoScheme)]
    [SameAnswerAsBlacklist]
    [ProducesResponseType(typeof(OidcUserInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOidcUserInfo(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(GetOidcUserInfoQuery.FromPrincipal(User), cancellationToken);

        if (result.IsError)
        {
            BearerTokenRejection.Reject(HttpContext, ChallengeReasonCodes.TokenRevoked);
            return new EmptyResult();
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(result.Value);
    }

    /// <summary>
    /// Revokes an access or refresh token (RFC 7009).
    /// </summary>
    /// <param name="request">The token to revoke.</param>
    /// <returns>Success status</returns>
    [HttpPost("revoke")]
    [AllowAnonymous]
    // The media type RFC 7009 mandates, and the one the discovery document
    // promises by listing this address as revocation_endpoint.
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeToken([FromForm] RevokeTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var command = new RevokeTokenCommand(
            request.Token,
            request.ParsedTokenTypeHint,
            userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => Ok(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Introspects a token and returns its metadata (RFC 7662).
    /// </summary>
    /// <param name="request">The token to introspect.</param>
    /// <returns>Token metadata including active status</returns>
    [HttpPost("introspect")]
    [Authorize]
    // The media type RFC 7662 mandates; see the note on revoke above.
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(IntrospectTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IntrospectToken([FromForm] IntrospectTokenRequest request, CancellationToken cancellationToken)
    {
        var query = new IntrospectTokenQuery(
            request.Token,
            request.ParsedTokenTypeHint);

        var result = await _sender.Send(query, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Sends a verification OTP to the authenticated user's email.
    /// </summary>
    /// <returns>OTP expiration time and masked email</returns>
    [HttpPost("send-verification-email")]
    [Authorize]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(SendEmailVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendVerificationEmail(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new SendEmailVerificationCommand(userId);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Verifies a user's email address using a 6-digit OTP.
    /// The anonymous (email-keyed) path also signs the user in and returns a
    /// login response; the admin (user-id-keyed) path returns 204 No Content.
    /// </summary>
    /// <param name="request">User ID or email address, and the OTP code</param>
    /// <returns>A login response for the self-service path, or no content for the admin path.</returns>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var command = new VerifyEmailCommand(
            request.UserId,
            request.Otp,
            request.Email,
            GetClientIpAddress(),
            GetUserAgent(),
            GetDeviceId(request.DeviceId));
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response =>
            {
                if (response.Login is null)
                {
                    return NoContent();
                }

                return Ok(response.Login);
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Resends a verification OTP to the specified email address.
    /// </summary>
    /// <param name="request">Email address</param>
    /// <returns>OTP expiration time and masked email</returns>
    [HttpPost("resend-verification-email")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ResendEmailVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendEmailVerificationRequest request, CancellationToken cancellationToken)
    {
        var command = new ResendEmailVerificationCommand(request.Email);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Step 1 of the public no-login deletion flow: request a verification
    /// code for an account's email address. Always acknowledges generically —
    /// whether the account exists is never revealed.
    /// </summary>
    [HttpPost("deletion/request")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestPublicDeletion(
        [FromBody] PublicDeletionRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new PublicRequestDeletionCommand(request.Email), cancellationToken);

        return result.Match<IActionResult>(
            _ => Accepted(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Step 2 of the public no-login deletion flow: confirm email possession
    /// with the verification code and schedule the deletion (30-day grace,
    /// then irreversible destruction). Confirming an already-pending deletion
    /// succeeds idempotently.
    /// </summary>
    [HttpPost("deletion/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmPublicDeletion(
        [FromBody] ConfirmPublicDeletionRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ConfirmPublicDeletionCommand(request.Email, request.OtpCode), cancellationToken);

        return result.Match<IActionResult>(
            _ => Accepted(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Recovers an account pending deletion during its grace window,
    /// authenticated by password (and TOTP when 2FA is enabled). Success
    /// cancels the deletion, restores the account and signs the user in.
    /// </summary>
    [HttpPost("deletion/recover")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RecoverAccount(
        [FromBody] RecoverAccountRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RecoverAccountCommand(
                request.Email,
                request.Password,
                request.TwoFactorCode,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                GetDeviceId()),
            cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Recovers an account pending deletion during its grace window,
    /// authenticated by an external identity provider's ID token (passwordless
    /// accounts). Success cancels the deletion, restores the account and signs
    /// the user in.
    /// </summary>
    [HttpPost("deletion/recover-external")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RecoverAccountExternal(
        [FromBody] RecoverAccountExternalRequest request, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RecoverAccountExternalCommand(
                request.Provider,
                request.IdToken,
                request.Nonce,
                request.TwoFactorCode,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                GetDeviceId(),
                ExternalNonceCookie.Read(Request)),
            cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    private string? GetAccessToken()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader["Bearer ".Length..].Trim();
    }
}
