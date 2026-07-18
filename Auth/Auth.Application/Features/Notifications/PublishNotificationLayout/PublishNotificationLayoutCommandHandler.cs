using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PublishNotificationLayout;

/// <summary>
/// Handler for publishing a layout. The draft is validated by rendering it with
/// probe content first — a broken layout breaks every email, so it can never go
/// live unchecked. Beyond syntax, the probe asserts the rendered output actually
/// contains the message body: a layout whose author removed the content slot
/// would otherwise publish successfully and blank every subsequent message.
/// </summary>
public class PublishNotificationLayoutCommandHandler
    : IRequestHandler<PublishNotificationLayoutCommand, ErrorOr<NotificationLayoutDto>>
{
    // Alphanumeric-plus-hyphens on purpose: the marker survives HTML encoding,
    // so the check also passes for a layout using {{ content }} without | raw.
    private const string ContentProbeMarker = "NOTIFICATION-LAYOUT-CONTENT-PROBE-93C2";
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly INotificationRenderer _renderer;
    private readonly ITemplateCacheInvalidator _cacheInvalidator;
    private readonly ILogger<PublishNotificationLayoutCommandHandler> _logger;

    public PublishNotificationLayoutCommandHandler(
        INotificationLayoutRepository layoutRepository,
        IApplicationRepository applicationRepository,
        INotificationRenderer renderer,
        ITemplateCacheInvalidator cacheInvalidator,
        ILogger<PublishNotificationLayoutCommandHandler> logger)
    {
        _layoutRepository = layoutRepository;
        _applicationRepository = applicationRepository;
        _renderer = renderer;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationLayoutDto>> Handle(
        PublishNotificationLayoutCommand request,
        CancellationToken cancellationToken)
    {
        var layout = await _layoutRepository.GetByIdAsync(request.LayoutId, cancellationToken);
        if (layout is null)
        {
            return NotificationErrors.LayoutNotFound(request.LayoutId);
        }

        // Publish gate: the draft must compose cleanly with probe content...
        var probe = await _renderer.RenderContentAsync(
            new NotificationContentRenderRequest
            {
                Channel = layout.Channel,
                ApplicationId = layout.ApplicationId,
                LanguageCode = "en",
                Subject = "Preview",
                BodyHtml = $"<p>{ContentProbeMarker}</p>",
                LayoutContentOverride = layout.DraftContent,
                LayoutStringsJsonOverride = layout.DraftStringsJson
            },
            cancellationToken);
        if (probe.IsError)
        {
            return probe.Errors;
        }

        // ...and the probe body must actually appear in the output — otherwise
        // the content slot is missing and every message would arrive empty.
        if (!probe.Value.BodyHtml.Contains(ContentProbeMarker, StringComparison.Ordinal))
        {
            return NotificationErrors.LayoutContentSlotMissing;
        }

        var result = layout.Publish(request.PublishedBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _layoutRepository.UpdateAsync(layout, cancellationToken);
        _cacheInvalidator.InvalidateLayout(layout.Channel, layout.ApplicationId);

        _logger.LogInformation(
            "Notification layout published: {LayoutId} (application {ApplicationId}) by {PublishedBy}",
            layout.Id, layout.ApplicationId, request.PublishedBy);

        string? applicationName = null;
        if (layout.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToLayoutDto(layout, applicationName);
    }
}
