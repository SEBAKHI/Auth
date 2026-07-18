namespace Auth_API.Modules.NotificationManagement.Contracts;

/// <summary>
/// Request to preview a layout draft buffer with placeholder body content.
/// </summary>
public record PreviewNotificationLayoutRequest(
    string LayoutContent,
    string LayoutStringsJson = "{}",
    string LanguageCode = "en");
