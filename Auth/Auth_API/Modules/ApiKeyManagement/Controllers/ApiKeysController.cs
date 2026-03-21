using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.ApiKeyManagement.Contracts;
using Auth.Application.Features.ApiKeys.CreateApiKey;
using Auth.Application.Features.ApiKeys.GetApiKeys;
using Auth.Application.Features.ApiKeys.RevokeApiKey;
using Auth.Application.Features.ApiKeys.RotateApiKey;
using Auth.Application.Features.ApiKeys.ValidateApiKey;
using Auth.Application.DTOs;
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
public class ApiKeysController : ApiController
{
    private readonly ISender _sender;

    public ApiKeysController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get all API keys for an application.
    /// </summary>
    [HttpGet]
    [RequirePermission("apikeys:read")]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetApiKeys([FromQuery] Guid applicationId, CancellationToken cancellationToken)
    {
        var query = new GetApiKeysQuery(applicationId);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            apiKeys => Ok(apiKeys),
            errors => Problem(errors));
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
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request, CancellationToken cancellationToken)
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

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            apiKey => CreatedAtAction(nameof(GetApiKeys), new { applicationId = request.ApplicationId }, apiKey),
            errors => Problem(errors));
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
    public async Task<IActionResult> RevokeApiKey(Guid id, [FromBody] RevokeApiKeyRequest? request = null, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var command = new RevokeApiKeyCommand(id, request?.Reason)
        {
            RevokedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Validate an API key and return its metadata.
    /// </summary>
    [HttpPost("validate")]
    [RequirePermission("apikeys:validate")]
    [ProducesResponseType(typeof(ValidateApiKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var query = new ValidateApiKeyQuery(request.ApiKey);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            response => Ok(response),
            errors => Problem(errors));
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
    public async Task<IActionResult> RotateApiKey(Guid id, [FromBody] RotateApiKeyRequest? request = null, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var command = new RotateApiKeyCommand(
            id,
            request?.GracePeriodMinutes ?? 60,
            userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            response => Ok(response),
            errors => Problem(errors));
    }

}
