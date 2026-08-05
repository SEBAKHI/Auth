namespace Auth.Application.Features.PrivacyPolicy.Common;

/// <summary>
/// The languages the privacy policy is published in — the same set the
/// frontend supports. A document may exist for any of these; the neutral
/// language is the fallback when a requested one is unwritten.
/// </summary>
public static class PolicyLanguages
{
    public const string Fallback = "en";

    /// <summary>
    /// Every published language, in the order the switcher lists them. Ordered
    /// rather than a bare set because it drives rendered output: a set's
    /// enumeration order would let the same content produce a different
    /// document — and therefore a different content hash — between runs.
    /// </summary>
    public static readonly IReadOnlyList<string> Ordered =
        ["en", "ar", "tr", "fr", "zh", "ur", "fa"];

    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);

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
