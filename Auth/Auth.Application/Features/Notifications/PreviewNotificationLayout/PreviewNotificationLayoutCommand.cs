using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PreviewNotificationLayout;

/// <summary>
/// Command rendering a layout draft buffer with placeholder body content, so the
/// layout editor previews exactly what templates will be wrapped in.
/// </summary>
/// <remarks>
/// The card colours pick the logo background the preview shows. Previewing builds nothing:
/// a colour whose background has not been built yet (saving the draft builds it) shows the
/// platform name as text, exactly as a send would.
/// </remarks>
public record PreviewNotificationLayoutCommand(
    string LayoutContent,
    string LayoutStringsJson,
    string LanguageCode,
    string? CardColorLight = null,
    string? CardColorDark = null) : IRequest<ErrorOr<NotificationPreviewDto>>;
