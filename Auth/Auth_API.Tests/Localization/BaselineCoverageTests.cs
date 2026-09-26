using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Auth_Localization.Extensions;
using Xunit;

namespace Auth_API.Tests.Localization;

/// <summary>
/// Guards the localization baseline: every resource key referenced from C# has an entry, and every
/// culture carries the same keys and the same format placeholders as the neutral resx.
/// <para>
/// These tests read the .resx sources directly instead of going through <c>ResourceManager</c>:
/// <c>GetString(key, "ar")</c> falls back to the neutral value when the Arabic key is absent, which
/// is precisely the drift being guarded against — the fallback would mask every failure here.
/// </para>
/// <para>
/// Complements <see cref="DomainErrorResourceCoverageTests"/>, which proves every domain
/// <c>Error.Code</c> resolves in the neutral DomainErrors.resx. Key parity below extends that
/// guarantee to the remaining cultures without re-running the reflection walk.
/// </para>
/// </summary>
public class BaselineCoverageTests
{
    /// <summary>Resource family name mapped to its resx path, relative to the solution root, without extension.</summary>
    private static readonly Dictionary<string, string> FamilyPaths = new(StringComparer.Ordinal)
    {
        ["AuthMessages"] = "Auth_Localization/Resources/AuthMessages",
        ["DomainErrors"] = "Auth_Localization/Resources/Errors/DomainErrors",
        ["MiddlewareMessages"] = "Auth_Localization/Resources/Middleware/MiddlewareMessages",
        // Email content is no longer resx-based: notification templates and their
        // translations live in the database (NotificationTemplates feature).
    };

    /// <summary>
    /// Derived from the runtime culture list rather than a duplicate literal, so adding an eighth
    /// language to <see cref="LocalizationServiceExtensions.SupportedCultures"/> fails these tests
    /// until its resx files exist. "en" is the neutral resx and carries no culture suffix.
    /// </summary>
    private static readonly string[] LocalizedCultures = LocalizationServiceExtensions.SupportedCultures
        .Where(c => !string.Equals(c, "en", StringComparison.OrdinalIgnoreCase))
        .ToArray();

    /// <summary>
    /// Matches composite-format placeholders, consuming "{{" and "}}" escapes first so an escaped
    /// brace is never mistaken for a placeholder. Group 1 captures the argument index.
    /// </summary>
    private static readonly Regex PlaceholderPattern =
        new(@"\{\{|\}\}|\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    private static readonly Lazy<string> SolutionRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !directory.EnumerateFiles("Auth.sln").Any())
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Auth.sln was not found walking up from '{AppContext.BaseDirectory}'.");
    });

    public static TheoryData<string, string> FamilyCultureMatrix
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var family in FamilyPaths.Keys)
            {
                foreach (var culture in LocalizedCultures)
                {
                    data.Add(family, culture);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FamilyCultureMatrix))]
    public void EveryCulture_DeclaresTheSameKeys_AsNeutral(string family, string culture)
    {
        var neutral = ReadResx(ResxPath(family, culture: null)).Keys;
        var localized = ReadResx(ResxPath(family, culture)).Keys;

        var missing = neutral.Except(localized, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal).ToList();
        var unexpected = localized.Except(neutral, StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty($"{family}.{culture}.resx is missing keys the neutral resx declares");
        unexpected.Should().BeEmpty($"{family}.{culture}.resx declares keys absent from the neutral resx");
    }

    /// <summary>
    /// A translator dropping or inventing a "{N}" produces a FormatException in that culture alone —
    /// invisible until a user on that language hits the path. The frontend already guards this
    /// (packages/i18n/src/locales/locales.test.ts); this is the server-side equivalent.
    /// </summary>
    [Theory]
    [MemberData(nameof(FamilyCultureMatrix))]
    public void EveryCulture_PreservesPlaceholderIndices_OfNeutral(string family, string culture)
    {
        var neutral = ReadResx(ResxPath(family, culture: null));
        var localized = ReadResx(ResxPath(family, culture));

        var mismatches = new List<string>();
        foreach (var (key, neutralValue) in neutral)
        {
            // Absent keys are reported by EveryCulture_DeclaresTheSameKeys_AsNeutral; don't double-report.
            if (!localized.TryGetValue(key, out var localizedValue))
            {
                continue;
            }

            var expected = PlaceholderIndices(neutralValue);
            var actual = PlaceholderIndices(localizedValue);

            if (!expected.SetEquals(actual))
            {
                mismatches.Add($"{key}: neutral has [{Format(expected)}] but {culture} has [{Format(actual)}]");
            }
        }

        mismatches.Should().BeEmpty($"a placeholder mismatch throws FormatException for {culture} users only");
    }

    private static string Format(IEnumerable<int> indices) =>
        string.Join(", ", indices.OrderBy(i => i).Select(i => $"{{{i}}}"));

    private static HashSet<int> PlaceholderIndices(string value)
    {
        var indices = new HashSet<int>();
        foreach (System.Text.RegularExpressions.Match match in PlaceholderPattern.Matches(value))
        {
            if (match.Groups[1].Success)
            {
                indices.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            }
        }

        return indices;
    }

    private static string ResxPath(string family, string? culture)
    {
        var suffix = culture is null ? ".resx" : $".{culture}.resx";
        return Path.Combine(SolutionRoot.Value, FamilyPaths[family].Replace('/', Path.DirectorySeparatorChar) + suffix);
    }

    /// <summary>Reads the string entries of a resx. Non-string entries carry a "type" attribute and are skipped.</summary>
    private static Dictionary<string, string> ReadResx(string path)
    {
        File.Exists(path).Should().BeTrue($"expected resource file '{path}' to exist");

        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(element => element.Attribute("type") is null)
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }
}
