using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PreviewNotificationTemplate;

/// <summary>
/// Command rendering supplied content (typically the editor's current buffer)
/// through the real pipeline: the type's sample data (plus optional overrides),
/// the scope's published layout, and the language's direction. Nothing is saved.
/// </summary>
public record PreviewNotificationTemplateCommand(
    Guid NotificationTypeId,
    string LanguageCode,
    string Subject,
    string BodyHtml,
    string? BodyText = null,
    Guid? ApplicationId = null,
    NotificationChannelType Channel = NotificationChannelType.Email,
    string? SampleOverridesJson = null) : IRequest<ErrorOr<NotificationPreviewDto>>;
