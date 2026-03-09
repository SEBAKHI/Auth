using Asp.Versioning;
using Auth_API.Authorization;
using Auth_Lib.Application.Features.ApiKeys.CreateApiKey;
using Auth_Lib.Application.Features.ApiKeys.GetApiKeys;
using Auth_Lib.Application.Features.ApiKeys.RevokeApiKey;
using Auth_Lib.Application.Features.ApiKeys.RotateApiKey;
using Auth_Lib.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.ApiKeyManagement.Controllers;

/// <summary>
/// Controller for API key management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class ApiKeysController : ControllerBase
{
    private readonly IMediator _mediator;

    public ApiKeysController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all API keys for an application.
    /// </summary>
    [HttpGet]
    [RequirePermission("apikeys:read")]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApiKeys([FromQuery] Guid applicationId)
    {
        var query = new GetApiKeysQuery(applicationId);
        var result = await _mediator.Send(query);

        return result.Match(
            apiKeys => Ok(apiKeys),
            errors => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: errors.First().Description));
    }

    /// <summary>
    /// Create a new API key.
    /// </summary>
    [HttpPost]
    [RequirePermission("apikeys:create")]
    [ProducesResponseType(typeof(CreateApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request)
    {
        var userId = GetCurrentUserId();
        var command = new CreateApiKeyCommand(
            request.ApplicationId,
            request.Name,
            request.Description,
            request.Environment ?? "production",
            request.RateLimitPerMinute ?? 60,
            request.RateLimitPerDay ?? 10000,
            request.ExpiresAt,
            request.PermissionIds)
        {
            CreatedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match(
            apiKey => CreatedAtAction(nameof(GetApiKeys), new { applicationId = request.ApplicationId }, apiKey),
            errors => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description));
    }

    /// <summary>
    /// Revoke an API key.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    [RequirePermission("apikeys:revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeApiKey(Guid id, [FromBody] RevokeApiKeyRequest? request = null)
    {
        var userId = GetCurrentUserId();
        var command = new RevokeApiKeyCommand(id, request?.Reason)
        {
            RevokedBy = userId
        };

        var result = await _mediator.Send(command);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    /// <summary>
    /// Rotate an API key, generating a new key while optionally keeping the old key valid for a grace period.
    /// </summary>
    [HttpPost("{id:guid}/rotate")]
    [RequirePermission("apikeys:rotate")]
    [ProducesResponseType(typeof(RotateApiKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RotateApiKey(Guid id, [FromBody] RotateApiKeyRequest? request = null)
    {
        var userId = GetCurrentUserId();
        var command = new RotateApiKeyCommand(
            id,
            request?.GracePeriodMinutes ?? 60,
            userId);

        var result = await _mediator.Send(command);

        return result.Match(
            response => Ok(response),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Validation => BadRequest(new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

// Request DTOs
public record CreateApiKeyRequest(
    Guid ApplicationId,
    string Name,
    string? Description = null,
    string? Environment = null,
    int? RateLimitPerMinute = null,
    int? RateLimitPerDay = null,
    DateTime? ExpiresAt = null,
    IReadOnlyList<Guid>? PermissionIds = null);

public record RevokeApiKeyRequest(string? Reason = null);

public record RotateApiKeyRequest(int? GracePeriodMinutes = 60);
