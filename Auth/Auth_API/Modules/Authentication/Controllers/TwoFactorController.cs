using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth_API.Common;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.Authentication.Contracts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// Two-factor authentication endpoints.
/// </summary>
/// <remarks>
/// Setup, enable and disable change the account's second factor — and the email
/// code serves the first enable — so each first asks for a recent sign-in: a
/// session older than
/// <c>TwoFactor:ReauthenticationMaxAgeMinutes</c> answers 403
/// <c>Auth.ReauthenticationRequired</c> before anything else runs.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/2fa")]
[Produces("application/json")]
[Authorize]
public class TwoFactorController : ApiController
{
    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;
    private readonly ILogger<TwoFactorController> _logger;

    public TwoFactorController(
        ISender sender,
        IOptionsSnapshot<IdentityProviderSettings> idpSettings,
        ILogger<TwoFactorController> logger)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sets up two-factor authentication by generating a secret and QR code.
    /// </summary>
    /// <returns>2FA setup information including QR code URI.</returns>
    [HttpPost("setup")]
    [ProducesResponseType(typeof(TwoFactorSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new SetupTwoFactorCommand(userId, GetCurrentSessionId());
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Emails the code an account enters before it switches on its FIRST second
    /// factor, to its confirmed address. A fresh code supersedes any earlier one.
    /// </summary>
    /// <remarks>
    /// Answers <c>emailCodeRequired: false</c>, and sends nothing, while no code is
    /// needed (email off, or the rollout switch). The code is never returned.
    /// </remarks>
    /// <returns>Whether a code is needed, the masked address it went to, and its expiry.</returns>
    [HttpPost("email-code")]
    [EnableRateLimiting("two-factor-email-code")]
    [ProducesResponseType(typeof(TwoFactorEmailCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendEmailCode(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new SendTwoFactorEmailCodeCommand(userId, GetCurrentSessionId(), GetClientIpAddress());
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Enables two-factor authentication after verifying a TOTP code, and — for
    /// the account's first second factor while email is on — the emailed code.
    /// </summary>
    /// <param name="request">The verification code, and the emailed code when one is needed.</param>
    /// <returns>Recovery codes for backup access.</returns>
    [HttpPost("enable")]
    [ProducesResponseType(typeof(EnableTwoFactorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enable([FromBody] TwoFactorVerifyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new EnableTwoFactorCommand(
            userId, request.Code, GetCurrentSessionId(), GetClientIpAddress(), request.EmailCode);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Completes a two-factor login by verifying a TOTP or recovery code
    /// against a pending login challenge, then issues tokens.
    /// </summary>
    /// <param name="request">The challenge token and verification code.</param>
    /// <returns>The full login response with tokens and user info.</returns>
    [HttpPost("verify")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Verify([FromBody] TwoFactorLoginVerifyRequest request, CancellationToken cancellationToken)
    {
        var command = new VerifyTwoFactorLoginCommand(
            request.ChallengeToken,
            request.Code,
            request.UseRecoveryCode,
            GetClientIpAddress(),
            GetUserAgent(),
            GetDeviceId(request.DeviceId));

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Disables two-factor authentication after verifying a code from the
    /// authenticator app or one of the recovery codes. Every other session and
    /// browser is signed out, and the owner is told by email.
    /// </summary>
    /// <param name="request">The verification code, and whether it is a recovery code.</param>
    /// <returns>Success status.</returns>
    [HttpPost("disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Disable([FromBody] TwoFactorDisableRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        // The caller's own session and SSO cookie are spared when the others are
        // signed out — the plain cookie value, as change-password passes it.
        var command = new DisableTwoFactorCommand(
            userId,
            request.Code,
            request.UseRecoveryCode,
            GetCurrentSessionId(),
            IdpSessionCookie.Read(Request, _idpSettings),
            GetClientIpAddress());
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }
}