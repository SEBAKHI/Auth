using System.Security.Claims;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Domain.Constants;
using Auth_API.Common;
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
        IOptions<IdentityProviderSettings> idpSettings,
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
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new SetupTwoFactorCommand(userId);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Enables two-factor authentication after verifying a TOTP code.
    /// </summary>
    /// <param name="request">The verification code.</param>
    /// <returns>Recovery codes for backup access.</returns>
    [HttpPost("enable")]
    [ProducesResponseType(typeof(EnableTwoFactorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Enable([FromBody] TwoFactorVerifyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new EnableTwoFactorCommand(userId, request.Code);
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
    /// Disables two-factor authentication after verifying a TOTP code.
    /// </summary>
    /// <param name="request">The verification code.</param>
    /// <returns>Success status.</returns>
    [HttpPost("disable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Disable([FromBody] TwoFactorVerifyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var command = new DisableTwoFactorCommand(userId, request.Code);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }


}