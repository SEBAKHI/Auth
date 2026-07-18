using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationLayoutDraft;

/// <summary>
/// Command to save layout draft edits (live layout untouched until publish).
/// </summary>
public record UpdateNotificationLayoutDraftCommand(
    Guid LayoutId,
    string Name,
    string DraftContent,
    string DraftStringsJson,
    DateTime? ExpectedModifiedAt = null) : IRequest<ErrorOr<NotificationLayoutDto>>
{
    public Guid ModifiedBy { get; init; }
}
