using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.CreateNotificationLayout;

/// <summary>
/// Handler for creating a notification layout.
/// </summary>
public class CreateNotificationLayoutCommandHandler
    : IRequestHandler<CreateNotificationLayoutCommand, ErrorOr<NotificationLayoutDto>>
{
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ITemplateRenderer _renderer;
    private readonly EmailLogoRenditions _emailLogoRenditions;
    private readonly ILogger<CreateNotificationLayoutCommandHandler> _logger;

    public CreateNotificationLayoutCommandHandler(
        INotificationLayoutRepository layoutRepository,
        IApplicationRepository applicationRepository,
        ITemplateRenderer renderer,
        EmailLogoRenditions emailLogoRenditions,
        ILogger<CreateNotificationLayoutCommandHandler> logger)
    {
        _layoutRepository = layoutRepository;
        _applicationRepository = applicationRepository;
        _renderer = renderer;
        _emailLogoRenditions = emailLogoRenditions;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationLayoutDto>> Handle(
        CreateNotificationLayoutCommand request,
        CancellationToken cancellationToken)
    {
        string? applicationName = null;
        if (request.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            if (application is null)
            {
                return ApplicationErrors.NotFound(applicationId);
            }

            applicationName = application.Name;
        }

        var exists = await _layoutRepository.ExistsAsync(request.ApplicationId, request.Channel, cancellationToken);
        if (exists)
        {
            return NotificationErrors.DuplicateLayout;
        }

        var syntax = _renderer.Validate(request.DraftContent);
        if (syntax.IsError)
        {
            return syntax.Errors;
        }

        var light = CardColor.CreateOptional(request.DraftCardColorLight);
        if (light.IsError)
        {
            return light.Errors;
        }

        var dark = CardColor.CreateOptional(request.DraftCardColorDark);
        if (dark.IsError)
        {
            return dark.Errors;
        }

        var layoutResult = NotificationLayout.Create(
            request.ApplicationId,
            request.Channel,
            request.Name,
            request.DraftContent,
            request.DraftStringsJson,
            light.Value,
            dark.Value,
            request.CreatedBy);
        if (layoutResult.IsError)
        {
            return layoutResult.Errors;
        }

        var layout = layoutResult.Value;
        await _layoutRepository.CreateAsync(layout, cancellationToken);

        if (layout.Channel == NotificationChannelType.Email)
        {
            await _emailLogoRenditions.EnsureAsync(layout.DraftCardColors, cancellationToken);
        }

        _logger.LogInformation(
            "Notification layout created: {LayoutId} (application {ApplicationId}, channel {Channel})",
            layout.Id, layout.ApplicationId, layout.Channel);

        return NotificationMapping.ToLayoutDto(layout, applicationName);
    }
}
