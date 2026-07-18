namespace Auth.Application.DTOs;

/// <summary>
/// A server-rendered preview: exactly what a real send would produce for the
/// given content, language, and layout scope.
/// </summary>
public class NotificationPreviewDto
{
    public string Subject { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;

    /// <summary>
    /// "ltr" or "rtl", derived from the language.
    /// </summary>
    public string Direction { get; set; } = "ltr";
}
