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
        ["ValidationMessages"] = "Auth_Localization/Resources/Validation/ValidationMessages",
        ["MiddlewareMessages"] = "Auth_Localization/Resources/Middleware/MiddlewareMessages",
        ["EmailTemplates"] = "Auth_Localization/Resources/Email/EmailTemplates",
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
    /// Matches any "Validation.*" string literal in the Application layer. Both reference styles put
    /// the resource key in the error *description*, which ApiController.LocalizeError then looks up:
    /// FluentValidation's <c>WithMessage("Validation.X")</c> and PasswordValidator's
    /// <c>Error.Validation("Password.X", "Validation.X")</c>. Matching the literal rather than one
    /// call shape covers both, and any third style added later.
    /// </summary>
    private static readonly Regex ValidationKeyPattern =
        new(@"""(Validation\.[A-Za-z0-9_.]+)""", RegexOptions.Compiled);

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

    /// <summary>
    /// ValidationMessages uses the resource key as the FluentValidation message, so a missing key
    /// does not degrade to English prose — the raw key is shown to the user in every language.
    /// This is the guard that <c>Validation.Days.Range</c> lacked for 3.5 months.
    /// </summary>
    [Fact]
    public void EveryValidationKeyReferencedInCode_HasNeutralResourceEntry()
    {
        var declared = ReadResx(ResxPath("ValidationMessages", culture: null)).Keys;

        var missing = DiscoverReferencedValidationKeys()
            .Distinct(StringComparer.Ordinal)
            .Where(key => !declared.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "every \"Validation.*\" key referenced from the Application layer must resolve — an unresolved key "
            + "is rendered verbatim to the user in every language — but these have no entry: {0}",
            string.Join(", ", missing));
    }

    /// <summary>Every referenced key must also be reachable — an unused key is dead weight that drifts.</summary>
    [Fact]
    public void EveryNeutralValidationKey_IsReferencedInCode()
    {
        var referenced = DiscoverReferencedValidationKeys().ToHashSet(StringComparer.Ordinal);

        var orphaned = ReadResx(ResxPath("ValidationMessages", culture: null)).Keys
            .Where(key => !referenced.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        orphaned.Should().BeEmpty(
            "an unreferenced validation key is dead weight and silently rots, but these are unused: {0}",
            string.Join(", ", orphaned));
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

    private static IEnumerable<string> DiscoverReferencedValidationKeys()
    {
        var applicationRoot = Path.Combine(SolutionRoot.Value, "Auth.Application");

        foreach (var file in Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file))
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match match in ValidationKeyPattern.Matches(File.ReadAllText(file)))
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static bool IsBuildArtifact(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }

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
