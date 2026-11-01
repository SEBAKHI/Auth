using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// The languages an administrator hid from the language menus of both apps: a hidden language
/// is neither listed nor downloaded, and a page that would open in it opens in English. It
/// hides a language from the interface only; content editors (notification translations,
/// sign-in design strings) keep every language.
/// </summary>
/// <remarks>
/// <see cref="Languages.Default"/> is never among them, so at least one language always stays
/// visible. The codes are distinct, lower-case and in the order of <see cref="Languages.Supported"/>,
/// so two saves of the same choice compare equal however they were written.
/// </remarks>
public sealed record class HiddenLanguages
{
    /// <summary>Nothing hidden: every supported language is listed.</summary>
    public static HiddenLanguages None { get; } = new([]);

    /// <summary>Gets the hidden language codes, in the order of <see cref="Languages.Supported"/>.</summary>
    public IReadOnlyList<string> Codes { get; }

    private HiddenLanguages(IReadOnlyList<string> codes) => Codes = codes;

    /// <summary>
    /// The languages an administrator chose to hide; null or empty hides none.
    /// <c>HiddenLanguages.UnsupportedLanguage</c> for a code the apps do not ship (a regional
    /// form such as <c>ar-SA</c> included), <c>HiddenLanguages.DefaultLanguageRequired</c> for
    /// English. Case and repetition do not matter.
    /// </summary>
    public static ErrorOr<HiddenLanguages> Create(IEnumerable<string?>? codes)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes ?? [])
        {
            var checkedCode = Check(code);
            if (checkedCode.IsError)
            {
                return checkedCode.Errors;
            }

            hidden.Add(checkedCode.Value);
        }

        return Ordered(hidden);
    }

    /// <summary>
    /// Rebuilds a stored list. What the rules refuse (an unknown code, English) is dropped rather
    /// than failing the read: every page renders this value, and a row edited by hand must not
    /// hide English or take the language menus down.
    /// </summary>
    public static HiddenLanguages FromStored(IEnumerable<string?>? codes) =>
        Ordered((codes ?? [])
            .Select(Check)
            .Where(code => !code.IsError)
            .Select(code => code.Value)
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>One code as the rules read it: lower-cased and trimmed, shipped, and not English.</summary>
    private static ErrorOr<string> Check(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        if (normalized is null || !Languages.Supported.Contains(normalized))
        {
            return HiddenLanguagesErrors.UnsupportedLanguage;
        }

        return normalized == Languages.Default
            ? HiddenLanguagesErrors.DefaultLanguageRequired
            : normalized;
    }

    private static HiddenLanguages Ordered(IReadOnlySet<string> hidden) =>
        hidden.Count == 0 ? None : new HiddenLanguages([.. Languages.Supported.Where(hidden.Contains)]);

    /// <inheritdoc />
    public bool Equals(HiddenLanguages? other) =>
        other is not null && Codes.SequenceEqual(other.Codes, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var code in Codes)
        {
            hash.Add(code, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
