using System.Text.RegularExpressions;
using Auth.Domain.Constants;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the seed data behind the two password notices to the constants that send them.
///
/// Nothing else in the solution does. The startup check only asks whether a system type has a
/// published global Email template - it never looks at the translations, so a type published in
/// English alone passes it, and an Arabic-speaking account silently receives English. The claim
/// that every template ships in seven languages lives, otherwise, in PRINT statements.
///
/// The BOM assertion belongs to the same class of invisible failure. These files carry Arabic,
/// Chinese, Urdu and Persian, and sqlcmd mangles them without a byte-order mark; a Windows tool
/// that rewrites one of these files without it produces a clean build and garbled mail.
/// </summary>
public class PasswordNotificationSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";

    /// <summary>The seeded type id, template id and version id for each new code.</summary>
    public static TheoryData<string, string, string> PasswordTypes => new()
    {
        {
            NotificationTypeCodes.PasswordCreated,
            "40000000-0000-0000-0000-000000000017",
            "0017"
        },
        {
            NotificationTypeCodes.PasswordChanged,
            "40000000-0000-0000-0000-000000000018",
            "0018"
        },
    };

    [Theory]
    [MemberData(nameof(PasswordTypes))]
    public void EachPasswordType_IsSeededWithItsCode(string code, string typeId, string _)
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));

        seed.Should().Contain(typeId, $"{code} needs a NotificationTypes row");
        seed.Should().Contain($"N'{code}'",
            "calling code resolves the type by code, so the seeded code must match the constant");
    }

    [Theory]
    [MemberData(nameof(PasswordTypes))]
    public void EachPasswordTemplate_ShipsSevenTranslationsAndIsPublished(
        string code, string typeId, string ordinal)
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var templateId = $"42000000-0000-0000-0000-0000000000{ordinal[2..]}";
        var versionId = $"43000000-0000-0000-0000-0000000000{ordinal[2..]}";

        seed.Should().Contain($"VALUES ('{templateId}', '{typeId}', NULL, 1, N'en'",
            $"{code} needs a global Email template bound to its type");
        seed.Should().Contain(versionId, $"{code} needs a version row");

        // One translation row per supported language, matched on the id ladder rather than on
        // the language codes alone, so a copy-paste that reuses another template's version id
        // cannot pass by accident.
        var translations = Regex
            .Matches(seed, $@"'44000000-0000-0000-{ordinal}-0000000000(\d\d)', '{versionId}', N'([a-z]{{2}})'")
            .Select(match => match.Groups[2].Value)
            .ToList();

        translations.Should().BeEquivalentTo(Languages.Supported,
            $"{code} must ship in every supported language, not only the default one");

        seed.Should().Contain(
            $"SET [PublishedVersionId] = '{versionId}'",
            $"an unpublished {code} template fails the renderer at send time, not at deploy time");
    }

    [Theory]
    [InlineData(TypesSeed)]
    [InlineData(TemplatesSeed)]
    public void SeedFile_KeepsItsByteOrderMark(string fileName)
    {
        var bytes = File.ReadAllBytes(SeedPath(fileName));

        (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeTrue(
            "the seeded copy is Arabic, Chinese, Urdu and Persian, and sqlcmd mangles it "
            + "without a BOM");
    }

    [Theory]
    [InlineData(TypesSeed)]
    [InlineData(TemplatesSeed)]
    public void SeedFile_IsIncludedInThePostDeploymentScript(string fileName)
    {
        // A <None> entry in the sqlproj is project visibility, not execution: only the :r
        // include runs. New notification types go in these two files precisely because both
        // are already included - no upgrade script is needed, and none should be added.
        var postDeployment = File.ReadAllText(Path.Combine(
            DbScriptsDirectory(), "..", "PostDeployment", "Script.PostDeployment.sql"));

        postDeployment.Should().Contain(fileName);
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(DbScriptsDirectory(), "SeedData", fileName);

    private static string DbScriptsDirectory() =>
        Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Scripts");

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
