using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.NotificationManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.GetNotificationTypes;
using Auth.Application.Features.Notifications.UpdateNotificationType;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// Notification types: the seeded categories with their variable catalogs and
/// preview sample data.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notification-types")]
[Authorize]
public class NotificationTypesController : ApiController
{
    private readonly ISender _sender;

    public NotificationTypesController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets all notification types.
    /// </summary>
    [HttpGet]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(List<NotificationTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTypes(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetNotificationTypesQuery(), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Updates a type's admin-editable metadata (name, description, sample data).
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(NotificationTypeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateType(
        Guid id,
        [FromBody] UpdateNotificationTypeRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateNotificationTypeCommand(
            id, request.Name, request.Description, request.VariablesJson, request.SampleDataJson)
        {
            ModifiedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }
}
