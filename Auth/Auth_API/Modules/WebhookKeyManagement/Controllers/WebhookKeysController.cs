using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.WebhookKeyManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.WebhookKeys.CreateWebhookKey;
using Auth.Application.Features.WebhookKeys.GetWebhookKeys;
using Auth.Application.Features.WebhookKeys.RevokeWebhookKey;
using Auth.Application.Features.WebhookKeys.RotateWebhookKey;
using Auth.Application.Features.WebhookKeys.ValidateWebhookKey;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.WebhookKeyManagement.Controllers;

/// <summary>
/// Controller for webhook key management operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
public class WebhookKeysController : ApiController
{
    private readonly ISender _sender;

    public WebhookKeysController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// List webhook keys, optionally narrowed to one application.
    /// </summary>
    /// <remarks>
    /// Omitting applicationId returns every application's keys, mirroring the API keys
    /// endpoint so the two pages behave identically.
    /// </remarks>
    [HttpGet]
    [RequirePermission(PermissionCodes.WebhookKeys.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWebhookKeys(
        [FromQuery] Guid? applicationId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetWebhookKeysQuery(applicationId, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            webhookKeys => Ok(webhookKeys),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a new webhook key.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.WebhookKeys.Create)]
    [ProducesResponseType(typeof(CreateWebhookKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateWebhookKey([FromBody] CreateWebhookKeyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new CreateWebhookKeyCommand(
            request.ApplicationId,
            request.Name,
            request.TargetUrl,
            request.Description,
            request.Environment ?? "production",
            request.ExpiresAt)
        {
            CreatedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            webhookKey => CreatedAtAction(nameof(GetWebhookKeys), new { applicationId = request.ApplicationId }, webhookKey),
            errors => Problem(errors));
    }

    /// <summary>
    /// Validate a webhook key and return its metadata.
    /// </summary>
    [HttpPost("validate")]
    [RequirePermission(PermissionCodes.WebhookKeys.Validate)]
    [ProducesResponseType(typeof(ValidateWebhookKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ValidateWebhookKey([FromBody] ValidateWebhookKeyRequest request, CancellationToken cancellationToken)
    {
        var query = new ValidateWebhookKeyQuery(request.WebhookKey);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            response => Ok(response),
            errors => Problem(errors));
    }

    /// <summary>
    /// Revoke a webhook key.
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    [RequirePermission(PermissionCodes.WebhookKeys.Revoke)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeWebhookKey(Guid id, [FromBody] RevokeWebhookKeyRequest? request = null, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var command = new RevokeWebhookKeyCommand(id, request?.Reason)
        {
            RevokedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Rotate a webhook key, generating a new key while optionally keeping the old key valid for a grace period.
    /// </summary>
    [HttpPost("{id:guid}/rotate")]
    [RequirePermission(PermissionCodes.WebhookKeys.Rotate)]
    [ProducesResponseType(typeof(RotateWebhookKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RotateWebhookKey(Guid id, [FromBody] RotateWebhookKeyRequest? request = null, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var command = new RotateWebhookKeyCommand(
            id,
            request?.GracePeriodMinutes ?? 60,
            userId);

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            response => Ok(response),
            errors => Problem(errors));
    }
}
