using System.Net;
using System.Text.RegularExpressions;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// Derives a readable plain-text alternative from a rendered HTML email body.
/// Used when a translation has no explicit BodyText template.
/// </summary>
public static partial class HtmlToTextConverter
{
    [GeneratedRegex(@"<\s*(script|style)[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"<\s*a\s[^>]*href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)<\s*/\s*a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<\s*br\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"<\s*/\s*(p|div|h1|h2|h3|h4|h5|h6|li|tr)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockCloseRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessNewlinesRegex();

    /// <summary>
    /// Converts rendered HTML to plain text: links become "text (url)", block
    /// boundaries become newlines, tags are stripped, and entities are decoded.
    /// </summary>
    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = ScriptStyleRegex().Replace(html, string.Empty);

        text = AnchorRegex().Replace(text, match =>
        {
            var href = match.Groups[1].Value.Trim();
            var inner = AnyTagRegex().Replace(match.Groups[2].Value, string.Empty).Trim();

            if (string.IsNullOrEmpty(inner) ||
                string.Equals(inner, href, StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }

            return $"{inner} ({href})";
        });

        text = LineBreakRegex().Replace(text, "\n");
        text = BlockCloseRegex().Replace(text, "\n\n");
        text = AnyTagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(line => HorizontalWhitespaceRegex().Replace(line, " ").Trim());
        text = string.Join('\n', lines);
        text = ExcessNewlinesRegex().Replace(text, "\n\n");

        return text.Trim();
    }
}
