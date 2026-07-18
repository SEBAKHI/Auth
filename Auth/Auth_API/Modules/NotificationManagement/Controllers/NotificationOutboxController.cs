using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.GetNotificationOutboxMessageById;
using Auth.Application.Features.Notifications.GetNotificationOutboxMessages;
using Auth.Application.Features.Notifications.RetryNotificationOutboxMessage;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// The notification delivery log: what was sent (or failed), from which
/// application, by which template version, to whom, in which language, and who
/// triggered it. Failed messages can be requeued for immediate retry.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notification-outbox")]
[Authorize]
public class NotificationOutboxController : ApiController
{
    private readonly ISender _sender;

    public NotificationOutboxController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets the paged delivery log with status/channel filters and search.
    /// </summary>
    [HttpGet]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(PagedNotificationOutboxDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMessages(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] NotificationDeliveryStatus? status = null,
        [FromQuery] NotificationChannelType? channel = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Desc,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new GetNotificationOutboxMessagesQuery(
                pageNumber, pageSize, status, channel, searchTerm, sortBy, sortDirection),
            cancellationToken);

        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets one delivery-log entry including the exact rendered content.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationOutboxMessageDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessage(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetNotificationOutboxMessageByIdQuery(id), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Requeues a failed (Retry/Dead) message for immediate dispatch.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var command = new RetryNotificationOutboxMessageCommand(id) { RequestedBy = GetCurrentUserId() };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(_ => NoContent(), Problem);
    }
}
