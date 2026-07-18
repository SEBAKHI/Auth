namespace Auth.Domain.Constants;

/// <summary>
/// Supported display languages and their text direction. Single source of truth
/// for the notification system; must stay in sync with the localization layer's
/// SupportedCultures and the frontend SUPPORTED_LANGUAGES list.
/// </summary>
public static class Languages
{
    public const string Default = "en";

    public static readonly IReadOnlyList<string> Supported =
        ["en", "ar", "tr", "fr", "zh", "ur", "fa"];

    /// <summary>
    /// Right-to-left languages ("he" kept for forward compatibility even though
    /// it is not currently in the supported set).
    /// </summary>
    public static readonly IReadOnlyList<string> Rtl = ["ar", "fa", "ur", "he"];

    public static bool IsSupported(string? languageCode) =>
        !string.IsNullOrWhiteSpace(languageCode) &&
        Supported.Contains(languageCode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns "rtl" or "ltr" for the given language code.
    /// </summary>
    public static string GetDirection(string languageCode) =>
        Rtl.Contains(languageCode, StringComparer.OrdinalIgnoreCase) ? "rtl" : "ltr";

    /// <summary>
    /// Normalizes a language code to its lowercase two-letter form when supported;
    /// returns null otherwise.
    /// </summary>
    public static string? Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var normalized = languageCode.Trim().ToLowerInvariant();

        // Accept culture forms like "ar-SA" by taking the language part.
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex > 0)
        {
            normalized = normalized[..dashIndex];
        }

        return Supported.Contains(normalized) ? normalized : null;
    }
}
