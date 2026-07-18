using Auth.Domain.Enums;

namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to render an editor buffer server-side with sample data and the
/// scope's published layout. Nothing is saved.
/// </summary>
public record PreviewNotificationTemplateRequest(
    Guid NotificationTypeId,
    string LanguageCode,
    string Subject,
    string BodyHtml,
    string? BodyText = null,
    Guid? ApplicationId = null,
    NotificationChannelType Channel = NotificationChannelType.Email,
    string? SampleOverridesJson = null);
