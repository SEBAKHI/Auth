using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The wire form of <see cref="PresentationHints"/>: the parameter names a
/// relying party sends on authorize and end-session, and the same names on the
/// accounts page those requests lead to.
/// </summary>
/// <remarks>
/// Only resolved values travel on to the accounts app (a supported language
/// code, a known colour mode), so nothing a client wrote reaches the page.
/// Never appended to the resume URL: the stored request carries the hints, and
/// the accounts app refuses a resume URL with any parameter it did not write.
/// </remarks>
public static class PresentationHintsQuery
{
    /// <summary>The language parameter (OpenID Connect Core §3.1.2.1).</summary>
    public const string UiLocales = "ui_locales";

    /// <summary>The colour-mode parameter: <c>light</c>, <c>dark</c> or <c>system</c>.</summary>
    public const string UiTheme = "ui_theme";

    /// <summary>
    /// <paramref name="url"/> with each present hint appended; unchanged when
    /// there is none.
    /// </summary>
    public static string AppendTo(string url, PresentationHints hints)
    {
        if (hints.Language is { } language)
        {
            url = RelyingPartyRedirect.AppendQuery(url, UiLocales, language);
        }

        if (hints.Theme is { } theme)
        {
            url = RelyingPartyRedirect.AppendQuery(url, UiTheme, theme);
        }

        return url;
    }
}
