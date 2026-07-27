using System.Security.Claims;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.ChangePassword;
using Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;
using Auth.Application.Features.AccountDeletion.PublicRequestDeletion;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.Logout;
using Auth.Application.Features.Authentication.ExternalLogin;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Features.Authentication.Register;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.ResendEmailVerification;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Application.Features.Authentication.SendEmailVerification;
using Auth.Application.Features.Authentication.TerminateAllSessions;
using Auth.Application.Features.Authentication.TerminateSession;
using Auth.Application.Features.Authentication.VerifyEmail;
using Auth_API.Modules.Authentication.Contracts;
using Auth.Application.Features.Authentication.GetUserSessions;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using MediatR;
using Auth_API.Common;
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
        IOptions<IdentityProviderSettings> idpSettings,
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
            request.DeviceId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response =>
            {
                IdpSessionCookie.Apply(Response, response, _idpSettings);
                return Ok(response);
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Registers a new user account with email and password.
    /// Creates a personal organization and sends email verification.
    /// </summary>
    /// <param name="request">Registration details</param>
    /// <returns>Registration confirmation with user ID and masked email</returns>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterCommand(
            request.Email,
            request.Password,
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.PhoneNumber,
            request.PreferredLanguage,
            request.TimeZone,
            request.CreateOrganization);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => StatusCode(StatusCodes.Status201Created, response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Returns enabled external authentication providers for UI rendering.
    /// </summary>
    /// <returns>List of enabled providers with code, name, and icon URL</returns>
    [HttpGet("external-providers")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<ExternalAuthProviderResponse>), StatusCodes.Status200OK)]
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
    /// Authenticates a user via an external provider (e.g., Google).
    /// Creates a new account if the user doesn't exist, or logs in if they do.
    /// </summary>
    /// <param name="request">External provider token and details</param>
    /// <returns>JWT tokens and user information</returns>
    [HttpPost("external-login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
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
            GetUserAgent());

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response =>
            {
                IdpSessionCookie.Apply(Response, response, _idpSettings);
                return Ok(response);
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Refreshes an access token using a valid refresh token.
    /// </summary>
    /// <param name="request">Refresh token</param>
    /// <returns>New JWT tokens</returns>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var command = new RefreshTokenCommand(
            request.RefreshToken,
            GetClientIpAddress(),
            GetUserAgent());

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
        CancellationToken cancellationToken)
    {
        // Rebuild the authorize URL from the CONFIGURED public origin, not from
        // Request.Host: behind the gateway the host is the internal destination
        // (identity.astoom.com), and the accounts app rejects a returnTo whose
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
            maxAge);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Redirect(response.RedirectUrl),
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
                    GetUserAgent());

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
                        RefreshExpiresIn = response.RefreshExpiresIn
                    }),
                    errors => Problem(errors));
            }

            default:
                return Problem([Auth.Domain.Errors.AuthErrors.UnsupportedGrantType]);
        }
    }

    /// <summary>
    /// Logs out the current user and revokes their tokens.
    /// </summary>
    /// <param name="request">Logout options</param>
    /// <returns>Success status</returns>
    [HttpPost("logout")]
    [Authorize]
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
            GetCurrentSessionId());

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
            request.TerminateSessions);

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
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> TerminateAllSessions(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        // Exclude current session
        var command = new TerminateAllSessionsCommand(userId, GetCurrentSessionId());
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            count => Ok(new { terminatedCount = count }),
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
            Permissions = User.FindAll(JwtClaimNames.Permissions).Select(c => c.Value).ToList()
        };

        return Ok(userInfo);
    }


    private Guid? GetCurrentSessionId()
    {
        // The stable session id lives in the "sid" claim (constant across token
        // refreshes); fall back to the legacy "jti" for tokens issued before sid.
        var sessionIdClaim = User.FindFirstValue(JwtClaimNames.Sid)
                             ?? User.FindFirstValue(JwtClaimNames.JwtId);

        if (Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            return sessionId;
        }

        return null;
    }

    /// <summary>
    /// Revokes an access or refresh token (RFC 7009).
    /// </summary>
    /// <param name="request">The token to revoke.</param>
    /// <returns>Success status</returns>
    [HttpPost("revoke")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var command = new RevokeTokenCommand(
            request.Token,
            request.TokenTypeHint,
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
    [ProducesResponseType(typeof(IntrospectTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> IntrospectToken([FromBody] IntrospectTokenRequest request, CancellationToken cancellationToken)
    {
        var query = new IntrospectTokenQuery(
            request.Token,
            request.TokenTypeHint);

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
            request.DeviceId);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response =>
            {
                if (response.Login is null)
                {
                    return NoContent();
                }

                IdpSessionCookie.Apply(Response, response.Login, _idpSettings);
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
                Request.Headers.UserAgent.ToString()),
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
                Request.Headers.UserAgent.ToString()),
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
