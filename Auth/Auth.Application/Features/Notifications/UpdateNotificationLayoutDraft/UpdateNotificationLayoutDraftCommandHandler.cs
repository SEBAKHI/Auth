using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
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
    private readonly ILogger<UpdateNotificationLayoutDraftCommandHandler> _logger;

    public UpdateNotificationLayoutDraftCommandHandler(
        INotificationLayoutRepository layoutRepository,
        IApplicationRepository applicationRepository,
        ITemplateRenderer renderer,
        ILogger<UpdateNotificationLayoutDraftCommandHandler> logger)
    {
        _layoutRepository = layoutRepository;
        _applicationRepository = applicationRepository;
        _renderer = renderer;
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

        var result = layout.UpdateDraft(request.Name, request.DraftContent, request.DraftStringsJson, request.ModifiedBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _layoutRepository.UpdateAsync(layout, cancellationToken);

        _logger.LogInformation("Notification layout draft saved: {LayoutId}", layout.Id);

        string? applicationName = null;
        if (layout.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToLayoutDto(layout, applicationName);
    }
}
