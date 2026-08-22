using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PublishNotificationLayout;

/// <summary>
/// Command to publish a layout draft: the draft columns are copied to the
/// published columns in one atomic update and the send-path cache is evicted.
/// </summary>
public record PublishNotificationLayoutCommand(Guid LayoutId, DateTime ExpectedRevisionAt)
    : IRequest<ErrorOr<NotificationLayoutDto>>
{
    public Guid PublishedBy { get; init; }
}
