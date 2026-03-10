using System.Security.Claims;
using Asp.Versioning;
using Auth.Application.Features.Authentication.DisableTwoFactor;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// Two-factor authentication endpoints.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/2fa")]
[Produces("application/json")]
[Authorize]
public class TwoFactorController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<TwoFactorController> _logger;

    public TwoFactorController(IMediator mediator, ILogger<TwoFactorController> logger)
    {
        _mediator = mediator;
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
    public async Task<IActionResult> Setup()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new SetupTwoFactorCommand(userId.Value);
        var result = await _mediator.Send(command);

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
    public async Task<IActionResult> Enable([FromBody] TwoFactorVerifyRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new EnableTwoFactorCommand(userId.Value, request.Code);
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            response => Ok(response),
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
    public async Task<IActionResult> Disable([FromBody] TwoFactorVerifyRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var command = new DisableTwoFactorCommand(userId.Value, request.Code);
        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
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

/// <summary>
/// Request model for two-factor verification.
/// </summary>
public record TwoFactorVerifyRequest
{
    /// <summary>
    /// The 6-digit TOTP code from the authenticator app.
    /// </summary>
    public required string Code { get; init; }
}
