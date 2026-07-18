namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to send a test message rendered with sample data. VersionId null =
/// the pending draft when present, else the published version.
/// </summary>
public record SendTestNotificationRequest(
    string LanguageCode,
    string RecipientEmail,
    Guid? VersionId = null);
