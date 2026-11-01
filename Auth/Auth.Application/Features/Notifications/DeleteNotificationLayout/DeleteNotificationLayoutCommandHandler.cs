using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.DeleteNotificationLayout;

/// <summary>
/// Handles <see cref="DeleteNotificationLayoutCommand"/>. The entity refuses the global layout, and
/// the delete statement itself only removes an application's layout, so no id can take a channel's
/// fallback away. The send-path cache entry of that scope is evicted afterwards: the next email of
/// the application resolves the global layout, the same way it does for an application that never
/// had a layout. A render already holding the cached layout finishes with it; nothing throws.
/// </summary>
public sealed class DeleteNotificationLayoutCommandHandler
    : IRequestHandler<DeleteNotificationLayoutCommand, ErrorOr<Deleted>>
{
    private readonly INotificationLayoutRepository _layouts;
    private readonly ITemplateCacheInvalidator _cacheInvalidator;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<DeleteNotificationLayoutCommandHandler> _logger;

    public DeleteNotificationLayoutCommandHandler(
        INotificationLayoutRepository layouts,
        ITemplateCacheInvalidator cacheInvalidator,
        IPublisher publisher,
        TimeProvider time,
        ILogger<DeleteNotificationLayoutCommandHandler> logger)
    {
        _layouts = layouts;
        _cacheInvalidator = cacheInvalidator;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(
        DeleteNotificationLayoutCommand request, CancellationToken cancellationToken)
    {
        var layout = await _layouts.GetByIdAsync(request.LayoutId, cancellationToken);
        if (layout is null)
        {
            return NotificationErrors.LayoutNotFound(request.LayoutId);
        }

        var deletable = layout.EnsureDeletable();
        if (deletable.IsError)
        {
            return deletable.Errors;
        }

        // The fallback has to exist: only the email channel has a seeded global layout, so an
        // application's SMS or push layout may be the only thing wrapping its messages.
        if (await _layouts.GetPublishedAsync(layout.Channel, null, cancellationToken) is null)
        {
            return NotificationErrors.LayoutFallbackMissing;
        }

        if (!await _layouts.DeleteApplicationLayoutAsync(layout.Id, cancellationToken))
        {
            // Deleted by someone else since it was read.
            return NotificationErrors.LayoutNotFound(layout.Id);
        }

        _cacheInvalidator.InvalidateLayout(layout.Channel, layout.ApplicationId);

        _logger.LogInformation(
            "Notification layout {LayoutId} (application {ApplicationId}) deleted by {DeletedBy}",
            layout.Id, layout.ApplicationId, request.DeletedBy);

        await _publisher.Publish(
            new NotificationLayoutDeletedEvent(
                Guid.NewGuid(),
                layout.Id,
                layout.ApplicationId,
                layout.Channel,
                layout.Name,
                request.DeletedBy,
                _time.GetUtcNow()),
            CancellationToken.None);

        return Result.Deleted;
    }
}
