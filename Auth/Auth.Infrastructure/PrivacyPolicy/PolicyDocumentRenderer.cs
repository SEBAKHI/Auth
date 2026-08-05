using System.Globalization;
using System.Net;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.DTOs;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Domain.Errors;
using Auth_Localization.Resources;
using ErrorOr;

namespace Auth.Infrastructure.PrivacyPolicy;

/// <summary>
/// Renders an authored policy document into a complete, self-contained HTML
/// page.
///
/// Deliberately not styled from the design system: this is a document to read,
/// print and archive, not an application screen. It carries its own inline
/// stylesheet so it renders with no network beyond itself, and no script at all
/// so it cannot fail the way the React page it replaces could.
/// </summary>
public partial class PolicyDocumentRenderer : IPolicyDocumentRenderer
{
    /// <summary>Scripts written right-to-left among the supported languages.</summary>
    private static readonly HashSet<string> RightToLeft =
        new(StringComparer.OrdinalIgnoreCase) { "ar", "fa", "ur" };

    /// <summary>
    /// Endonyms for the language switcher: a reader who cannot read the current
    /// document cannot be expected to recognise their language named in it.
    /// </summary>
    private static readonly Dictionary<string, string> LanguageNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English",
            ["ar"] = "العربية",
            ["tr"] = "Türkçe",
            ["fr"] = "Français",
            ["zh"] = "中文",
            ["ur"] = "اردو",
            ["fa"] = "فارسی"
        };

    /// <summary>
    /// Official source for every law the policy names. Order matters: the
    /// combined term must be offered before its parts, or "CCPA/CPRA" would
    /// match as "CCPA" and leave a dangling "/CPRA".
    /// </summary>
    private static readonly (string Term, string Url)[] LawLinks =
    [
        ("CCPA/CPRA", "https://leginfo.legislature.ca.gov/faces/codes_displayText.xhtml?division=3.&part=4.&lawCode=CIV&title=1.81.5"),
        ("RGPD", "https://eur-lex.europa.eu/eli/reg/2016/679/oj"),
        ("GDPR", "https://eur-lex.europa.eu/eli/reg/2016/679/oj"),
        ("KVKK", "https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=6698&MevzuatTur=1&MevzuatTertip=5"),
        ("CPRA", "https://leginfo.legislature.ca.gov/faces/codes_displayText.xhtml?division=3.&part=4.&lawCode=CIV&title=1.81.5"),
        ("CCPA", "https://leginfo.legislature.ca.gov/faces/codes_displayText.xhtml?division=3.&part=4.&lawCode=CIV&title=1.81.5")
    ];

    private static readonly Regex LawPattern = new(
        "(" + string.Join("|", LawLinks.Select(law => Regex.Escape(law.Term))) + ")",
        RegexOptions.Compiled);

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern { get; }

    /// <inheritdoc />
    public ErrorOr<RenderedPolicyDocument> Render(PolicyRenderRequest request)
    {
        var values = DisclosureValues(request.Disclosure);
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);

        string Text(string source) => Interpolate(source, values, unresolved);

        var body = new StringBuilder();
        AppendHeader(body, request, Text);

        if (request.IsFallbackLanguage)
        {
            body.Append("<aside class=\"notice\" role=\"note\">")
                .Append(Escape(FallbackNotice(request.LanguageCode)))
                .Append("</aside>");
        }

        AppendIntro(body, request.Content, Text);
        foreach (var section in request.Content.Sections) AppendSection(body, section, Text);
        AppendRetention(body, request.Content.Retention, Text);
        AppendDeletion(body, request, Text);
        foreach (var section in request.Content.Rights) AppendSection(body, section, Text);
        foreach (var section in request.Content.Closing) AppendSection(body, section, Text);
        AppendContactLines(body, request.Content, request.Disclosure);

        // A surviving token would be published verbatim inside a legal
        // disclosure, so it fails the publish rather than reaching a reader.
        if (unresolved.Count > 0)
        {
            return PrivacyPolicyErrors.InvalidContent(
                $"the '{request.LanguageCode}' document leaves {string.Join(", ", unresolved.Select(t => "{{" + t + "}}"))} unresolved");
        }

        var html = Wrap(request, Text(request.Content.Title), body.ToString());
        return new RenderedPolicyDocument(html, Sha256(html), StyleHash);
    }

    private static void AppendHeader(
        StringBuilder body, PolicyRenderRequest request, Func<string, string> text)
    {
        body.Append("<header><h1>").Append(Escape(text(request.Content.Title))).Append("</h1>")
            .Append("<p class=\"meta\"><span class=\"badge\">")
            .Append(Escape(text(request.Content.VersionLabel))).Append(' ')
            .Append(Escape(request.Version))
            .Append("</span><span>").Append(Escape(text(request.Content.EffectiveDate)))
            .Append("</span></p>");

        if (request.AvailableLanguages.Count > 1)
        {
            // Relative hrefs so the switcher stays inside whichever document set
            // the reader is in: "./en" resolves under /privacy/ and equally under
            // /privacy/v7/, keeping an archived version archived.
            body.Append("<nav class=\"langs\" aria-label=\"Languages\">");
            foreach (var code in request.AvailableLanguages)
            {
                var name = LanguageNames.TryGetValue(code, out var endonym) ? endonym : code;
                var current = string.Equals(code, request.LanguageCode, StringComparison.OrdinalIgnoreCase);
                body.Append("<a href=\"./").Append(Escape(code)).Append('"')
                    .Append(current ? " aria-current=\"page\"" : string.Empty)
                    .Append(" hreflang=\"").Append(Escape(code)).Append("\" lang=\"")
                    .Append(Escape(code)).Append("\" dir=\"")
                    .Append(RightToLeft.Contains(code) ? "rtl" : "ltr").Append("\">")
                    .Append(Escape(name)).Append("</a>");
            }
            body.Append("</nav>");
        }

        body.Append("</header>");
    }

    private static void AppendIntro(
        StringBuilder body, PolicyDocumentModel content, Func<string, string> text)
    {
        if (content.Intro.Count == 0) return;
        body.Append("<section class=\"intro\">");
        foreach (var paragraph in content.Intro) AppendParagraph(body, paragraph, text);
        body.Append("</section>");
    }

    private static void AppendSection(
        StringBuilder body, PolicyDocumentSection section, Func<string, string> text)
    {
        var heading = text(section.Heading);
        body.Append("<section id=\"").Append(Slug(heading)).Append("\"><h2>")
            .Append(WithLawLinks(Escape(heading))).Append("</h2>");

        foreach (var paragraph in section.Paragraphs) AppendParagraph(body, paragraph, text);

        if (section.Bullets is { Count: > 0 })
        {
            body.Append("<ul>");
            foreach (var bullet in section.Bullets)
            {
                body.Append("<li>").Append(WithLawLinks(Escape(text(bullet)))).Append("</li>");
            }
            body.Append("</ul>");
        }

        body.Append("</section>");
    }

    private static void AppendRetention(
        StringBuilder body, PolicyDocumentRetention retention, Func<string, string> text)
    {
        var heading = text(retention.Heading);
        body.Append("<section id=\"").Append(Slug(heading)).Append("\"><h2>")
            .Append(WithLawLinks(Escape(heading))).Append("</h2>");
        AppendParagraph(body, retention.Intro, text);

        body.Append("<div class=\"table-scroll\"><table><thead><tr>");
        foreach (var column in retention.Columns)
        {
            body.Append("<th scope=\"col\">").Append(Escape(text(column))).Append("</th>");
        }
        body.Append("</tr></thead><tbody>");

        foreach (var row in retention.Rows)
        {
            body.Append("<tr><th scope=\"row\">").Append(WithLawLinks(Escape(text(row.Category))))
                .Append("</th><td>").Append(WithLawLinks(Escape(text(row.Retention))))
                .Append("</td><td>").Append(WithLawLinks(Escape(text(row.Detail))))
                .Append("</td></tr>");
        }

        body.Append("</tbody></table></div></section>");
    }

    private static void AppendDeletion(
        StringBuilder body, PolicyRenderRequest request, Func<string, string> text)
    {
        var deletion = request.Content.Deletion;
        var heading = text(deletion.Heading);
        body.Append("<section id=\"").Append(Slug(heading)).Append("\"><h2>")
            .Append(WithLawLinks(Escape(heading))).Append("</h2>");

        foreach (var paragraph in deletion.Paragraphs) AppendParagraph(body, paragraph, text);

        if (deletion.Bullets.Count > 0)
        {
            body.Append("<ul>");
            foreach (var bullet in deletion.Bullets)
            {
                body.Append("<li>").Append(WithLawLinks(Escape(text(bullet)))).Append("</li>");
            }
            body.Append("</ul>");
        }

        // A link, not a button: the store-listing deletion route has to work
        // without script, which is the whole reason this page has none.
        var deleteUrl = request.AccountsBaseUrl.TrimEnd('/') + "/delete-account";
        body.Append("<p class=\"action\"><a class=\"danger\" href=\"").Append(Escape(deleteUrl))
            .Append("\">").Append(Escape(text(deletion.Button))).Append("</a></p>")
            .Append("<p class=\"hint\">").Append(Escape(text(deletion.SignedInHint)))
            .Append("</p></section>");
    }

    private static void AppendContactLines(
        StringBuilder body, PolicyDocumentModel content, PrivacyPolicyDisclosureDto disclosure)
    {
        // Conditionally required by law, so the system cannot decide for the
        // operator whether they apply. Whole lines that vanish when blank — a
        // label with nothing after it is worse than no line at all.
        (string Label, string? Value)[] optional =
        [
            (content.ContactDpoLabel, disclosure.DpoContact),
            (content.ContactVerbisLabel, disclosure.VerbisNo),
            (content.ContactKepLabel, disclosure.KepAddress)
        ];

        var shown = optional.Where(line => !string.IsNullOrWhiteSpace(line.Value)).ToList();
        if (shown.Count == 0) return;

        body.Append("<hr><section class=\"contact\">");
        foreach (var (label, value) in shown)
        {
            body.Append("<p>").Append(Escape(label)).Append(": ").Append(Escape(value!))
                .Append("</p>");
        }
        body.Append("</section>");
    }

    private static void AppendParagraph(
        StringBuilder body, string paragraph, Func<string, string> text)
    {
        if (string.IsNullOrWhiteSpace(paragraph)) return;
        body.Append("<p>").Append(WithLawLinks(Escape(text(paragraph)))).Append("</p>");
    }

    /// <summary>Maps every token an author may write to its published value.</summary>
    private static Dictionary<string, string> DisclosureValues(PrivacyPolicyDisclosureDto d) =>
        new(StringComparer.Ordinal)
        {
            ["graceDays"] = d.GraceDays.ToString(CultureInfo.InvariantCulture),
            ["otpValidityMinutes"] = d.OtpValidityMinutes.ToString(CultureInfo.InvariantCulture),
            ["loginAttemptRetentionDays"] = d.LoginAttemptRetentionDays.ToString(CultureInfo.InvariantCulture),
            ["outboxRetentionDays"] = d.OutboxRetentionDays.ToString(CultureInfo.InvariantCulture),
            ["identifierReservationDays"] = d.IdentifierReservationDays.ToString(CultureInfo.InvariantCulture),
            ["policyVersion"] = d.PolicyVersion ?? string.Empty,
            ["legalName"] = d.LegalName ?? string.Empty,
            ["address"] = d.Address ?? string.Empty,
            ["privacyEmail"] = d.PrivacyEmail ?? string.Empty,
            ["emailProvider"] = d.EmailProvider ?? string.Empty,
            ["hostingProvider"] = d.HostingProvider ?? string.Empty,
            ["hostingCountry"] = d.HostingCountry ?? string.Empty,
            ["dpoContact"] = d.DpoContact ?? string.Empty,
            ["verbisNo"] = d.VerbisNo ?? string.Empty,
            ["kepAddress"] = d.KepAddress ?? string.Empty
        };

    private static string Interpolate(
        string source,
        IReadOnlyDictionary<string, string> values,
        ISet<string> unresolved)
    {
        if (string.IsNullOrEmpty(source)) return string.Empty;

        return TokenPattern.Replace(source, match =>
        {
            var token = match.Groups[1].Value;
            if (values.TryGetValue(token, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Blank counts as unresolved too: a required disclosure rendering as
            // an empty gap is the silent version of the same defect.
            unresolved.Add(token);
            return match.Value;
        });
    }

    /// <summary>Links every law reference to its official text.</summary>
    private static string WithLawLinks(string escapedText)
    {
        if (string.IsNullOrEmpty(escapedText)) return escapedText;

        return LawPattern.Replace(escapedText, match =>
        {
            var law = LawLinks.First(candidate => candidate.Term == match.Value);
            return $"<a href=\"{WebUtility.HtmlEncode(law.Url)}\" target=\"_blank\" rel=\"noreferrer noopener\">{match.Value}</a>";
        });
    }

    private static readonly ResourceManager Messages = new(
        typeof(AuthMessages).FullName!, typeof(AuthMessages).Assembly);

    /// <summary>
    /// States, in the reader's own language, that they are being shown the
    /// neutral document. A reader who cannot read English is exactly the reader
    /// who cannot be told so in English.
    /// </summary>
    private static string FallbackNotice(string languageCode)
    {
        var culture = CultureInfo.GetCultureInfo(languageCode);
        return Messages.GetString("PolicyLanguageFallbackNotice", culture)
            ?? Messages.GetString("PolicyLanguageFallbackNotice", CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>
    /// Stable anchor for a heading, so a rights request can cite a section by
    /// URL. ASCII-only headings keep their words; anything else falls back to a
    /// hash, which is stable for the same text without guessing a
    /// transliteration.
    /// </summary>
    private static string Slug(string heading)
    {
        var builder = new StringBuilder();
        foreach (var character in heading.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(character);
            else if (char.IsWhiteSpace(character) || character == '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Length > 0 ? slug : "s-" + Sha256(heading)[..8];
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// CSP source expression for the inline stylesheet this renderer emits.
    /// Base64, not hex: that is the encoding the CSP hash grammar requires.
    /// </summary>
    private static readonly string StyleHash =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Styles)));

    private static string Wrap(PolicyRenderRequest request, string title, string body)
    {
        var dir = RightToLeft.Contains(request.LanguageCode) ? "rtl" : "ltr";

        return $"""
            <!DOCTYPE html>
            <html lang="{Escape(request.LanguageCode)}" dir="{dir}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Escape(title)}</title>
            <style>{Styles}</style>
            </head>
            <body>
            <main>{body}</main>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Inline, self-contained and deliberately plain. No web font and no
    /// external sheet: a legal notice that needs a second request to become
    /// readable is a legal notice that can fail to be readable.
    /// </summary>
    private const string Styles = """
        :root{color-scheme:light dark;--fg:#18181b;--muted:#52525b;--bg:#fff;--line:#e4e4e7;--accent:#18181b;--danger:#b91c1c}
        @media (prefers-color-scheme:dark){:root{--fg:#fafafa;--muted:#a1a1aa;--bg:#09090b;--line:#27272a;--accent:#fafafa;--danger:#f87171}}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.65 system-ui,-apple-system,"Segoe UI",Roboto,"Noto Naskh Arabic","Noto Sans Arabic",sans-serif;-webkit-text-size-adjust:100%}
        main{max-width:46rem;margin:0 auto;padding:3rem 1.25rem 4rem}
        header{text-align:center;margin-bottom:2.5rem}
        h1{font-size:1.6rem;line-height:1.3;margin:0 0 .75rem}
        h2{font-size:1.15rem;line-height:1.4;margin:2.25rem 0 .75rem}
        p{margin:0 0 .85rem;color:var(--muted)}
        .meta{display:flex;flex-wrap:wrap;gap:.5rem;align-items:center;justify-content:center;margin:0}
        .badge{border:1px solid var(--line);border-radius:999px;padding:.15rem .6rem;font-size:.8rem;color:var(--fg)}
        .meta span:not(.badge){font-size:.875rem}
        .langs{display:flex;flex-wrap:wrap;gap:.75rem;justify-content:center;margin-top:1rem;font-size:.875rem}
        .langs a{color:var(--muted)}
        .langs a[aria-current=page]{color:var(--fg);font-weight:600;text-decoration:none}
        .notice{border:1px solid var(--line);border-inline-start:3px solid var(--accent);border-radius:.5rem;padding:.75rem 1rem;margin:0 0 2rem;font-size:.9rem}
        ul{margin:0 0 .85rem;padding-inline-start:1.25rem;color:var(--muted)}
        li+li{margin-top:.4rem}
        a{color:inherit;text-underline-offset:.2em}
        .table-scroll{overflow-x:auto;margin:0 0 .85rem}
        table{border-collapse:collapse;width:100%;font-size:.9rem}
        th,td{border-bottom:1px solid var(--line);padding:.6rem .75rem;text-align:start;vertical-align:top;color:var(--muted)}
        thead th{color:var(--fg);font-weight:600;white-space:nowrap}
        tbody th{color:var(--fg);font-weight:500}
        hr{border:0;border-top:1px solid var(--line);margin:2.5rem 0 1.5rem}
        .action{margin:1.25rem 0 .5rem}
        .danger{display:inline-block;border:1px solid var(--danger);color:var(--danger);border-radius:999px;padding:.45rem 1.1rem;text-decoration:none;font-size:.9rem}
        .hint{font-size:.8rem}
        @media print{
          :root{--fg:#000;--muted:#000;--bg:#fff;--line:#999}
          main{max-width:none;padding:0}
          .langs,.action{display:none}
          a{text-decoration:none}
          a[href^="http"]::after{content:" (" attr(href) ")";font-size:.75em;word-break:break-all}
          section{break-inside:avoid}
        }
        """;
}
