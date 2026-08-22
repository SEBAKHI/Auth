using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.NotificationManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.CreateNotificationLayout;
using Auth.Application.Features.Notifications.GetNotificationLayoutById;
using Auth.Application.Features.Notifications.GetNotificationLayouts;
using Auth.Application.Features.Notifications.PreviewNotificationLayout;
using Auth.Application.Features.Notifications.PublishNotificationLayout;
using Auth.Application.Features.Notifications.UpdateNotificationLayoutDraft;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// Notification layouts: the shared visual identity (header/footer/CSS) wrapped
/// around every template body, per application/channel, all languages.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notification-layouts")]
[Authorize]
public class NotificationLayoutsController : ApiController
{
    private readonly ISender _sender;

    public NotificationLayoutsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets all layouts (global first).
    /// </summary>
    [HttpGet]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(List<NotificationLayoutDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLayouts(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetNotificationLayoutsQuery(), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets one layout for editing.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationLayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLayout(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetNotificationLayoutByIdQuery(id), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Creates an application-specific layout.
    /// </summary>
    [HttpPost]
    [RequirePermission("notification-layouts:manage")]
    [ProducesResponseType(typeof(NotificationLayoutDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateLayout(
        [FromBody] CreateNotificationLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateNotificationLayoutCommand(
            request.ApplicationId, request.Channel, request.Name,
            request.DraftContent, request.DraftStringsJson)
        {
            CreatedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(
            dto => CreatedAtAction(nameof(GetLayout), new { id = dto.Id, version = "1" }, dto),
            Problem);
    }

    /// <summary>
    /// Saves layout draft edits.
    /// </summary>
    [HttpPut("{id:guid}/draft")]
    [RequirePermission("notification-layouts:manage")]
    [ProducesResponseType(typeof(NotificationLayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        [FromBody] UpdateNotificationLayoutDraftRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateNotificationLayoutDraftCommand(
            id, request.Name, request.DraftContent, request.DraftStringsJson, request.ExpectedModifiedAt)
        {
            ModifiedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Publishes the layout draft (atomic copy to the live columns).
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [RequirePermission("notification-layouts:manage")]
    [ProducesResponseType(typeof(NotificationLayoutDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(
        Guid id,
        [FromBody] PublishNotificationLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var command = new PublishNotificationLayoutCommand(id, request.ExpectedRevisionAt)
        {
            PublishedBy = GetCurrentUserId()
        };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Previews a layout draft buffer with placeholder body content.
    /// </summary>
    [HttpPost("preview")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationPreviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromBody] PreviewNotificationLayoutRequest request,
        CancellationToken cancellationToken)
    {
        var command = new PreviewNotificationLayoutCommand(
            request.LayoutContent, request.LayoutStringsJson, request.LanguageCode);

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }
}
