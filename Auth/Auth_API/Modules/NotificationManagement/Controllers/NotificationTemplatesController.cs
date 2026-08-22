using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.NotificationManagement.Contracts;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.CreateNotificationTemplate;
using Auth.Application.Features.Notifications.DeleteNotificationTemplate;
using Auth.Application.Features.Notifications.DiscardNotificationTemplateDraft;
using Auth.Application.Features.Notifications.GetNotificationTemplateById;
using Auth.Application.Features.Notifications.GetNotificationTemplates;
using Auth.Application.Features.Notifications.GetNotificationTemplateVersion;
using Auth.Application.Features.Notifications.GetNotificationsSummary;
using Auth.Application.Features.Notifications.PreviewNotificationTemplate;
using Auth.Application.Features.Notifications.PublishNotificationTemplate;
using Auth.Application.Features.Notifications.RestoreNotificationTemplateVersion;
using Auth.Application.Features.Notifications.RollbackNotificationTemplate;
using Auth.Application.Features.Notifications.SendTestNotification;
using Auth.Application.Features.Notifications.UnpublishNotificationTemplate;
using Auth.Application.Features.Notifications.UpdateNotificationTemplateDraft;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// Admin management of notification templates: the database is the single source
/// of truth for all message content; everything here is editable without redeploy.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notification-templates")]
[Authorize]
public class NotificationTemplatesController : ApiController
{
    private readonly ISender _sender;

    public NotificationTemplatesController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets the paged template list with type/application/channel/status filters.
    /// </summary>
    [HttpGet]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(PagedNotificationTemplatesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? notificationTypeId = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery] NotificationChannelType? channel = null,
        [FromQuery] bool? isPublished = null,
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new GetNotificationTemplatesQuery(
                pageNumber, pageSize, notificationTypeId, applicationId,
                channel, isPublished, searchTerm, sortBy, sortDirection),
            cancellationToken);

        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets the notifications section overview: template, layout and delivery
    /// counts plus what is currently published.
    /// </summary>
    /// <remarks>
    /// Deliberately hosted under this controller rather than a notifications
    /// controller of its own: the gateway forwards an explicit per-feature
    /// allowlist, and this path rides the existing notification-templates route.
    /// The literal segment cannot collide with <c>{id:guid}</c>.
    /// </remarks>
    [HttpGet("summary")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationsSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetNotificationsSummaryQuery(), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets the full editor view of one template (versions + translations).
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetNotificationTemplateByIdQuery(id), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Gets one version's full translations (history preview / restore).
    /// </summary>
    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationTemplateVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTemplateVersion(
        Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new GetNotificationTemplateVersionQuery(id, versionId), cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Creates a template (empty draft v1).
    /// </summary>
    [HttpPost]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateNotificationTemplateCommand(
            request.NotificationTypeId, request.ApplicationId, request.Channel, request.DefaultLanguage)
        {
            CreatedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(
            dto => CreatedAtAction(nameof(GetTemplate), new { id = dto.Id, version = "1" }, dto),
            Problem);
    }

    /// <summary>
    /// Saves draft edits (translation upserts/removals + change note).
    /// </summary>
    [HttpPut("{id:guid}/draft")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDraft(
        Guid id,
        [FromBody] UpdateNotificationTemplateDraftRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UpdateNotificationTemplateDraftCommand(
            id,
            request.Translations
                .Select(t => new DraftTranslationInput(t.LanguageCode, t.Subject, t.BodyHtml, t.BodyText))
                .ToList(),
            request.RemoveLanguages,
            request.ChangeNote,
            request.ExpectedModifiedAt)
        {
            ModifiedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Discards the pending draft.
    /// </summary>
    [HttpDelete("{id:guid}/draft")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DiscardDraft(Guid id, CancellationToken cancellationToken)
    {
        var command = new DiscardNotificationTemplateDraftCommand(id) { DiscardedBy = GetCurrentUserId() };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Publishes the pending draft (all translations go live atomically).
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [RequirePermission("notification-templates:publish")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(
        Guid id,
        [FromBody] PublishNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new PublishNotificationTemplateCommand(
            id,
            request.ExpectedDraftVersionId,
            request.ExpectedRevisionAt)
        {
            PublishedBy = GetCurrentUserId()
        };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Unpublishes the template (forbidden for system-global templates).
    /// </summary>
    [HttpPost("{id:guid}/unpublish")]
    [RequirePermission("notification-templates:publish")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unpublish(
        Guid id,
        [FromBody] UnpublishNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new UnpublishNotificationTemplateCommand(id, request.ExpectedPublishedVersionId)
        {
            UnpublishedBy = GetCurrentUserId()
        };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Rolls the published pointer back to a previous version (all its
    /// translations return together).
    /// </summary>
    [HttpPost("{id:guid}/rollback")]
    [RequirePermission("notification-templates:publish")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rollback(
        Guid id,
        [FromBody] RollbackNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RollbackNotificationTemplateCommand(id, request.TargetVersionId)
        {
            RolledBackBy = GetCurrentUserId()
        };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Restores a historical version as a new editable draft.
    /// </summary>
    [HttpPost("{id:guid}/versions/{versionId:guid}/restore-draft")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(typeof(NotificationTemplateDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestoreVersionAsDraft(
        Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        var command = new RestoreNotificationTemplateVersionCommand(id, versionId)
        {
            RestoredBy = GetCurrentUserId()
        };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Deletes the template with its whole version history (forbidden for
    /// system-global templates).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken cancellationToken)
    {
        var command = new DeleteNotificationTemplateCommand(id) { DeletedBy = GetCurrentUserId() };
        var result = await _sender.Send(command, cancellationToken);
        return result.Match(_ => NoContent(), Problem);
    }

    /// <summary>
    /// Renders an editor buffer server-side (sample data + published layout) —
    /// the preview is exactly what a real send would produce.
    /// </summary>
    [HttpPost("preview")]
    [RequirePermission("notification-templates:read")]
    [ProducesResponseType(typeof(NotificationPreviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromBody] PreviewNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var command = new PreviewNotificationTemplateCommand(
            request.NotificationTypeId,
            request.LanguageCode,
            request.Subject,
            request.BodyHtml,
            request.BodyText,
            request.ApplicationId,
            request.Channel,
            request.SampleOverridesJson);

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(dto => Ok(dto), Problem);
    }

    /// <summary>
    /// Sends a test message rendered with sample data to the given address.
    /// </summary>
    [HttpPost("{id:guid}/test-send")]
    [RequirePermission("notification-templates:manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SendTest(
        Guid id,
        [FromBody] SendTestNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var command = new SendTestNotificationCommand(
            id, request.LanguageCode, request.RecipientEmail, request.VersionId)
        {
            RequestedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);
        return result.Match(_ => NoContent(), Problem);
    }
}
