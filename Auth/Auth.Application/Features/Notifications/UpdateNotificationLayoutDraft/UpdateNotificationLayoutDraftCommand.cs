using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationLayoutDraft;

/// <summary>
/// Command to save layout draft edits (live layout untouched until publish).
/// </summary>
/// <remarks>
/// Each card colour: null keeps the saved colour, an empty string returns to the default,
/// and anything else must be #RRGGBB.
/// </remarks>
public record UpdateNotificationLayoutDraftCommand(
    Guid LayoutId,
    string Name,
    string DraftContent,
    string DraftStringsJson,
    DateTime? ExpectedModifiedAt = null,
    string? DraftCardColorLight = null,
    string? DraftCardColorDark = null) : IRequest<ErrorOr<NotificationLayoutDto>>
{
    public Guid ModifiedBy { get; init; }
}
