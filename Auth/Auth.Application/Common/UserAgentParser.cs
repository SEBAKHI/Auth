using System.Text.RegularExpressions;

namespace Auth.Application.Common;

/// <summary>
/// Minimal user-agent classifier: turns a raw UA string into a browser/OS pair.
/// Heuristic by design — an unrecognised agent yields nulls rather than a guess.
///
/// This is a deliberate port of <c>Auth_UI/packages/ui/src/user-agent.ts</c>,
/// pattern for pattern and in the same order, and the two must stay in step. A
/// general-purpose UA library was rejected precisely because it would disagree:
/// the profile's session list is labelled by the client parser and the
/// new-device email by this one, and "Edge" in one place with "Chrome" in the
/// other reads as a second, unexplained sign-in.
/// </summary>
public static partial class UserAgentParser
{
    /// <summary>A browser/OS pair; either half may be unknown.</summary>
    public readonly record struct ParsedUserAgent(string? Browser, string? Os)
    {
        /// <summary>
        /// Human-readable label for the email body, e.g. "Chrome on Windows".
        /// Null when neither half could be identified — better to say nothing
        /// than to name the wrong device in a security notice.
        /// </summary>
        public string? Describe() => (Browser, Os) switch
        {
            (null, null) => null,
            (not null, null) => Browser,
            (null, not null) => Os,
            _ => $"{Browser} on {Os}"
        };
    }

    // Order matters: brands that embed other brands' tokens (Edge before
    // Chrome, Chrome before Safari, OPR before Chrome) are tested first.
    private static readonly (Regex Pattern, string Name)[] Browsers =
    [
        (EdgeRegex(), "Microsoft Edge"),
        (OperaRegex(), "Opera"),
        (SamsungRegex(), "Samsung Internet"),
        (FirefoxRegex(), "Firefox"),
        (ChromeRegex(), "Chrome"),
        (SafariRegex(), "Safari"),
    ];

    private static readonly (Regex Pattern, string Name)[] OperatingSystems =
    [
        (WindowsRegex(), "Windows"),
        (AndroidRegex(), "Android"),
        (IosRegex(), "iOS"),
        (MacRegex(), "macOS"),
        (ChromeOsRegex(), "ChromeOS"),
        (LinuxRegex(), "Linux"),
    ];

    /// <summary>Classifies a user-agent string.</summary>
    public static ParsedUserAgent Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new ParsedUserAgent(null, null);
        }

        string? browser = null;
        foreach (var (pattern, name) in Browsers)
        {
            if (pattern.IsMatch(userAgent))
            {
                browser = name;
                break;
            }
        }

        string? os = null;
        foreach (var (pattern, name) in OperatingSystems)
        {
            if (pattern.IsMatch(userAgent))
            {
                os = name;
                break;
            }
        }

        return new ParsedUserAgent(browser, os);
    }

    [GeneratedRegex(@"\bEdg(?:e|A|iOS)?/")]
    private static partial Regex EdgeRegex();

    [GeneratedRegex(@"\bOPR/|\bOpera\b")]
    private static partial Regex OperaRegex();

    [GeneratedRegex(@"\bSamsungBrowser/")]
    private static partial Regex SamsungRegex();

    [GeneratedRegex(@"\bFirefox/|\bFxiOS/")]
    private static partial Regex FirefoxRegex();

    [GeneratedRegex(@"(?:\b|Headless)Chrome/|\bCriOS/")]
    private static partial Regex ChromeRegex();

    [GeneratedRegex(@"\bSafari/")]
    private static partial Regex SafariRegex();

    [GeneratedRegex("Windows NT")]
    private static partial Regex WindowsRegex();

    [GeneratedRegex("Android")]
    private static partial Regex AndroidRegex();

    [GeneratedRegex("iPhone|iPad|iPod")]
    private static partial Regex IosRegex();

    [GeneratedRegex("Mac OS X|Macintosh")]
    private static partial Regex MacRegex();

    [GeneratedRegex("CrOS")]
    private static partial Regex ChromeOsRegex();

    [GeneratedRegex("Linux")]
    private static partial Regex LinuxRegex();
}
