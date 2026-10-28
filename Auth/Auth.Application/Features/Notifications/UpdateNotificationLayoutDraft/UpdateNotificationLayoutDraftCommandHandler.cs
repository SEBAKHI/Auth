using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationLayoutDraft;

/// <summary>
/// Handler for saving layout draft edits with syntax validation and optimistic
/// concurrency.
/// </summary>
public class UpdateNotificationLayoutDraftCommandHandler
    : IRequestHandler<UpdateNotificationLayoutDraftCommand, ErrorOr<NotificationLayoutDto>>
{
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ITemplateRenderer _renderer;
    private readonly EmailLogoRenditions _emailLogoRenditions;
    private readonly ILogger<UpdateNotificationLayoutDraftCommandHandler> _logger;

    public UpdateNotificationLayoutDraftCommandHandler(
        INotificationLayoutRepository layoutRepository,
        IApplicationRepository applicationRepository,
        ITemplateRenderer renderer,
        EmailLogoRenditions emailLogoRenditions,
        ILogger<UpdateNotificationLayoutDraftCommandHandler> logger)
    {
        _layoutRepository = layoutRepository;
        _applicationRepository = applicationRepository;
        _renderer = renderer;
        _emailLogoRenditions = emailLogoRenditions;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationLayoutDto>> Handle(
        UpdateNotificationLayoutDraftCommand request,
        CancellationToken cancellationToken)
    {
        var layout = await _layoutRepository.GetByIdAsync(request.LayoutId, cancellationToken);
        if (layout is null)
        {
            return NotificationErrors.LayoutNotFound(request.LayoutId);
        }

        if (request.ExpectedModifiedAt is { } expected &&
            layout.ModifiedAt is { } actual &&
            Math.Abs((actual - expected).TotalMilliseconds) > 1)
        {
            return NotificationErrors.ConcurrencyConflict;
        }

        var syntax = _renderer.Validate(request.DraftContent);
        if (syntax.IsError)
        {
            return syntax.Errors;
        }

        var light = Resolve(request.DraftCardColorLight, layout.DraftCardColorLight);
        if (light.IsError)
        {
            return light.Errors;
        }

        var dark = Resolve(request.DraftCardColorDark, layout.DraftCardColorDark);
        if (dark.IsError)
        {
            return dark.Errors;
        }

        var result = layout.UpdateDraft(
            request.Name,
            request.DraftContent,
            request.DraftStringsJson,
            light.Value,
            dark.Value,
            request.ModifiedBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _layoutRepository.UpdateAsync(layout, cancellationToken);

        // Built on save, not on preview: previewing needs only read access, and a read-only
        // caller must not be able to write a file per colour it cares to name.
        if (layout.Channel == NotificationChannelType.Email)
        {
            await _emailLogoRenditions.EnsureAsync(layout.DraftCardColors, cancellationToken);
        }

        _logger.LogInformation("Notification layout draft saved: {LayoutId}", layout.Id);

        string? applicationName = null;
        if (layout.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToLayoutDto(layout, applicationName);
    }

    /// <summary>
    /// An absent colour keeps the saved one; an empty one returns to the default; anything
    /// else must be <c>#RRGGBB</c>.
    /// </summary>
    /// <remarks>
    /// The colours arrived after the other draft fields, so a script that edits a layout, or a
    /// console tab opened before they existed, sends a draft without them. Treating that as
    /// "reset to the default" would erase an admin's colours without anyone choosing to, and
    /// the next publish would put the logo back in a box.
    /// </remarks>
    private static ErrorOr<CardColor?> Resolve(string? requested, CardColor? saved) =>
        requested is null ? saved : CardColor.CreateOptional(requested);
}
