using Auth.Domain.Enums;

namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to create a notification template. ApplicationId null = the global
/// fallback template for the type/channel.
/// </summary>
public record CreateNotificationTemplateRequest(
    Guid NotificationTypeId,
    Guid? ApplicationId,
    NotificationChannelType Channel = NotificationChannelType.Email,
    string DefaultLanguage = "en");
