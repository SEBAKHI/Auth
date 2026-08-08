using System.Text.RegularExpressions;
using Auth.Domain.Enums;

namespace Auth.Application.Common;

/// <summary>
/// Minimal user-agent classifier: turns a raw UA string into a browser/OS pair
/// and a form factor. Heuristic by design — an unrecognised agent yields nulls
/// rather than a guess.
///
/// The only parser in the system. There was a second one in the browser
/// (<c>Auth_UI/packages/ui/src/user-agent.ts</c>) that labelled the session list
/// while this one labelled the new-device email, with a comment on each asking
/// the next author to keep them in step by hand — "Edge" in one place and
/// "Chrome" in the other reads to a user as a second, unexplained sign-in. Both
/// labels are now derived here and persisted, so there is nothing left to drift.
///
/// A general-purpose UA library is still declined: the value is a short label a
/// non-technical reader has to recognise, not a taxonomy.
/// </summary>
public static partial class UserAgentParser
{
    /// <summary>A browser/OS pair plus a form factor; either name may be unknown.</summary>
    public readonly record struct ParsedUserAgent(string? Browser, string? Os, DeviceType DeviceType)
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
            // Not Desktop: an absent agent is a caller we cannot classify at all
            // (a script, a health probe), and guessing "computer" in the session
            // list would be a claim we have no evidence for.
            return new ParsedUserAgent(null, null, DeviceType.Unknown);
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

        // Tablet before mobile: an Android tablet's agent satisfies both tests,
        // and the tablet pattern is the more specific one.
        var deviceType = TabletRegex().IsMatch(userAgent)
            ? DeviceType.Tablet
            : MobileRegex().IsMatch(userAgent)
                ? DeviceType.Mobile
                : DeviceType.Desktop;

        return new ParsedUserAgent(browser, os, deviceType);
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

    [GeneratedRegex(@"\biPad\b|\bTablet\b|Android(?!.*Mobile)")]
    private static partial Regex TabletRegex();

    [GeneratedRegex(@"\bMobi|iPhone|iPod|Android.*Mobile")]
    private static partial Regex MobileRegex();
}
