namespace Auth.Application.Features.PrivacyPolicy.Common;

/// <summary>
/// The languages the privacy policy is published in — the same set the
/// frontend supports. A document may exist for any of these; the neutral
/// language is the fallback when a requested one is unwritten.
/// </summary>
public static class PolicyLanguages
{
    public const string Fallback = "en";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "en", "ar", "tr", "fr", "zh", "ur", "fa"
    };

    public static bool IsSupported(string? languageCode) =>
        !string.IsNullOrWhiteSpace(languageCode) && Supported.Contains(languageCode);

    /// <summary>Normalizes "tr-TR" to "tr"; returns null when unsupported.</summary>
    public static string? Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return null;
        var primary = languageCode.Split('-')[0].ToLowerInvariant();
        return Supported.Contains(primary) ? primary : null;
    }
}
