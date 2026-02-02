using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Modules.Authentication.Commands;
using Auth_API.Modules.Authentication.Commands.EmailVerification;
using Auth_API.Modules.Authentication.Contracts;
using Auth_API.Modules.Authentication.Queries;
using Auth_Lib.Constants;
using Auth_Lib.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// Authentication endpoints for login, logout, and token management.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IMediator mediator, ILogger<AuthController> logger)
    {
        _mediator = mediator;
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
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var command = new LoginCommand(
            request.Email,
            request.Password,
            GetClientIpAddress(),
            GetUserAgent(),
            request.DeviceId);

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            response => Ok(response),
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
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        var command = new RefreshTokenCommand(
            request.RefreshToken,
            GetClientIpAddress(),
            GetUserAgent());

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
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
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new LogoutCommand(
            userId.Value,
            request?.RefreshToken,
            GetAccessToken(),
            GetClientIpAddress(),
            request?.LogoutAllDevices ?? false);

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
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
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new ChangePasswordCommand(
            userId.Value,
            request.CurrentPassword,
            request.NewPassword,
            request.TerminateSessions,
            GetCurrentSessionId());

        var result = await _mediator.Send(command);

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
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var command = new ForgotPasswordCommand(request.Email);
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Resets a user's password using a reset token.
    /// </summary>
    /// <param name="request">Email, reset token and new password.</param>
    /// <returns>Success status</returns>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var command = new ResetPasswordCommand(
            request.Email,
            request.Token,
            request.NewPassword,
            request.TerminateSessions);

        var result = await _mediator.Send(command);

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
    public async Task<IActionResult> GetSessions()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var query = new GetUserSessionsQuery(userId.Value, GetCurrentSessionId());
        var result = await _mediator.Send(query);

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
    public async Task<IActionResult> TerminateSession(Guid sessionId)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new TerminateSessionCommand(userId.Value, sessionId);
        var result = await _mediator.Send(command);

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
    public async Task<IActionResult> TerminateAllSessions()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        // Exclude current session
        var command = new TerminateAllSessionsCommand(userId.Value, GetCurrentSessionId());
        var result = await _mediator.Send(command);

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
            Id = GetCurrentUserId() ?? Guid.Empty,
            Email = User.FindFirstValue(JwtClaimNames.Email) ?? string.Empty,
            FirstName = User.FindFirstValue(JwtClaimNames.GivenName) ?? string.Empty,
            LastName = User.FindFirstValue(JwtClaimNames.FamilyName) ?? string.Empty,
            DisplayName = User.FindFirstValue(JwtClaimNames.Name),
            PreferredLanguage = User.FindFirstValue(JwtClaimNames.Locale),
            TimeZone = User.FindFirstValue(JwtClaimNames.TimeZone),
            Roles = User.FindAll(JwtClaimNames.Roles).Select(c => c.Value).ToList(),
            Permissions = User.FindAll(JwtClaimNames.Permissions).Select(c => c.Value).ToList()
        };

        return Ok(userInfo);
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirstValue(JwtClaimNames.Subject);

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        return null;
    }

    private Guid? GetCurrentSessionId()
    {
        // Session ID is stored in the JWT token ID (jti) claim
        var sessionIdClaim = User.FindFirstValue(JwtClaimNames.JwtId);

        if (Guid.TryParse(sessionIdClaim, out var sessionId))
        {
            return sessionId;
        }

        return null;
    }

    private string? GetClientIpAddress()
    {
        // Check for forwarded header (when behind proxy/gateway)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').FirstOrDefault()?.Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        return Request.Headers.UserAgent.FirstOrDefault();
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
    public async Task<IActionResult> RevokeToken([FromBody] RevokeTokenRequest request)
    {
        var userId = GetCurrentUserId();

        var command = new RevokeTokenCommand(
            request.Token,
            request.TokenTypeHint,
            userId);

        var result = await _mediator.Send(command);

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
    public async Task<IActionResult> IntrospectToken([FromBody] IntrospectTokenRequest request)
    {
        var query = new IntrospectTokenQuery(
            request.Token,
            request.TokenTypeHint);

        var result = await _mediator.Send(query);

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
    public async Task<IActionResult> SendVerificationEmail()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new SendEmailVerificationCommand(userId.Value);
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Verifies a user's email address using a 6-digit OTP.
    /// </summary>
    /// <param name="request">User ID and OTP code</param>
    /// <returns>Success status</returns>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var command = new VerifyEmailCommand(request.UserId, request.Otp);
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
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
    public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendEmailVerificationRequest request)
    {
        var command = new ResendEmailVerificationCommand(request.Email);
        var result = await _mediator.Send(command);

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

    private IActionResult Problem(IEnumerable<ErrorOr.Error> errors)
    {
        var firstError = errors.First();

        var statusCode = firstError.Type switch
        {
            ErrorOr.ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorOr.ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorOr.ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorOr.ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorOr.ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = firstError.Code,
            Detail = firstError.Description,
            Instance = Request.Path
        };

        if (errors.Count() > 1)
        {
            problemDetails.Extensions["errors"] = errors.Select(e => new
            {
                code = e.Code,
                description = e.Description
            });
        }

        return StatusCode(statusCode, problemDetails);
    }
}
