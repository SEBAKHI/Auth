using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PreviewNotificationLayout;

/// <summary>
/// Command rendering a layout draft buffer with placeholder body content, so the
/// layout editor previews exactly what templates will be wrapped in.
/// </summary>
public record PreviewNotificationLayoutCommand(
    string LayoutContent,
    string LayoutStringsJson,
    string LanguageCode) : IRequest<ErrorOr<NotificationPreviewDto>>;
