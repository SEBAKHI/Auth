using Auth.Domain.Enums;

namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to create an application-specific notification layout.
/// </summary>
public record CreateNotificationLayoutRequest(
    Guid? ApplicationId,
    string Name,
    string DraftContent,
    string DraftStringsJson = "{}",
    NotificationChannelType Channel = NotificationChannelType.Email);
