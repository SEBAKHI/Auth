using Auth.Domain.Constants;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// How a relying party asks the accounts pages to look while its user is there:
/// in its language (<c>ui_locales</c>, OpenID Connect Core §3.1.2.1) and in its
/// colour mode (<c>ui_theme</c>, this provider's own parameter; no standard names
/// one).
/// </summary>
/// <remarks>
/// <para>
/// A hint, never a setting: it shapes the pages of one sign-in or sign-out and
/// is written to nothing the user owns. The profile's language and theme stay
/// the user's own choice, and the language of every message stays the
/// recipient's: the page is shown to whoever is at the browser, the mail goes to
/// the account's owner.
/// </para>
/// <para>
/// Nothing a client sends can fail here. OpenID Connect says an unsupported
/// <c>ui_locales</c> value is not an error, so an unusable value is dropped and
/// the request goes on. The accounts app resolves a direct link the same way;
/// <c>docs/api/ui-locales-cases.json</c> holds both resolvers to one table.
/// </para>
/// </remarks>
public sealed record class PresentationHints
{
    /// <summary>
    /// The language tags read from <c>ui_locales</c>; the rest are ignored, so the
    /// work stays bounded however long the parameter is.
    /// </summary>
    public const int MaxLocaleTags = 10;

    /// <summary>
    /// The longest language tag considered (RFC 5646's practical bound); a longer
    /// one is skipped but still counts toward <see cref="MaxLocaleTags"/>.
    /// </summary>
    public const int MaxLocaleTagLength = 35;

    /// <summary>The colour modes <c>ui_theme</c> may name, exactly as written.</summary>
    public static readonly IReadOnlyList<string> Themes = ["light", "dark", "system"];

    /// <summary>No hint: the accounts pages decide for themselves.</summary>
    public static PresentationHints None { get; } = new(null, null);

    private PresentationHints(string? language, string? theme)
    {
        Language = language;
        Theme = theme;
    }

    /// <summary>
    /// The page language: one of <see cref="Languages.Supported"/>, or null when
    /// the relying party named none.
    /// </summary>
    public string? Language { get; }

    /// <summary>The colour mode: one of <see cref="Themes"/>, or null when none was named.</summary>
    public string? Theme { get; }

    /// <summary>Whether neither hint is present.</summary>
    public bool IsEmpty => Language is null && Theme is null;

    /// <summary>
    /// Reads the two parameters as sent. A value already resolved (a stored
    /// request's) reads back as itself.
    /// </summary>
    public static PresentationHints From(string? uiLocales, string? uiTheme)
    {
        var language = ResolveLanguage(uiLocales);
        var theme = ResolveTheme(uiTheme);

        return language is null && theme is null ? None : new PresentationHints(language, theme);
    }

    /// <summary>
    /// The first supported language among the space-separated tags, in the
    /// order sent; English when tags were sent and none is supported; null when
    /// the parameter is absent or blank.
    /// </summary>
    private static string? ResolveLanguage(string? uiLocales)
    {
        if (string.IsNullOrWhiteSpace(uiLocales))
        {
            return null;
        }

        var tags = uiLocales.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var tag in tags.Take(MaxLocaleTags))
        {
            if (tag.Length <= MaxLocaleTagLength && Languages.Normalize(tag) is { } language)
            {
                return language;
            }
        }

        return Languages.Default;
    }

    private static string? ResolveTheme(string? uiTheme) =>
        uiTheme is not null && Themes.Contains(uiTheme, StringComparer.Ordinal) ? uiTheme : null;
}
