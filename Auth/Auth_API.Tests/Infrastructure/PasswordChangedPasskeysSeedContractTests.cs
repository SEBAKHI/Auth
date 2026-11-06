using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// S20 (P7-R12, D-T19 D): the password-changed notice's version 2 lists the passkeys that stay
/// after the change — name, when added, when last used — so the owner can spot one they do not
/// recognise. Rendered here through the real renderer in every language, with the model the
/// sender builds (<c>PasswordChangedEventHandler</c>): the names are the owner's own text and
/// must come out encoded.
/// </summary>
public class PasswordChangedPasskeysSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string TemplateId = "42000000-0000-0000-0000-000000000018";
    private const string Version1 = "43000000-0000-0000-0000-000000000018";
    private const string Version2 = "43000000-0000-0000-0002-000000000018";

    private readonly FluidTemplateRenderer _renderer = new(TimeProvider.System);

    [Fact]
    public void Version2_IsANewVersionPublishedOnlyWhileTheSeededVersion1IsThePublishedOne()
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var start = seed.IndexOf($"AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '{Version2}')", StringComparison.Ordinal);
        start.Should().BePositive("version 2 is inserted once, guarded by its own id");
        var block = seed[start..seed.IndexOf("\nEND", start, StringComparison.Ordinal)];

        block.Should().Contain("ISNULL(MAX([VersionNumber]), 0) + 1");
        Regex.Replace(block, @"\s+", " ").Should().Contain(
            $"UPDATE [dbo].[NotificationTemplates] SET [PublishedVersionId] = '{Version2}' WHERE [Id] = '{TemplateId}' "
            + $"AND [PublishedVersionId] = '{Version1}';");
        Translations(Version2).Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported);
    }

    [Fact]
    public void EveryTranslation_ListsThePasskeysThatStay_EncodingEachName()
    {
        foreach (var (language, subjectSource, bodySource) in Translations(Version2))
        {
            var model = Model(
                ("Work <laptop> & co", "2026-08-10 08:00:00Z", "2026-08-15 18:30:00Z"),
                ("Phone", "2026-07-01 07:00:00Z", null));

            var subject = _renderer.RenderTracking(subjectSource, model, language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, model, language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse();
            body.IsError.Should().BeFalse($"the {language} body must render: {(body.IsError ? body.FirstError.Description : "")}");
            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} body names nothing the sender does not pass");
            body.Value.Should().NotContain("{{").And.NotContain("{%");

            body.Value.Should().Contain("Work &lt;laptop&gt; &amp; co", $"the {language} notice encodes a passkey's name")
                .And.NotContain("<laptop>");
            body.Value.Should().Contain("Phone")
                .And.Contain("2026-08-10 08:00:00Z")
                .And.Contain("2026-08-15 18:30:00Z")
                .And.Contain("2026-07-01 07:00:00Z");
            body.Value.Should().Contain("Example Platform").And.Contain("2026-11-05 09:14:00Z");
        }
    }

    [Fact]
    public void WithoutAPasskey_TheNoticeIsVersion1sWords()
    {
        var version1 = Translations(Version1).ToDictionary(row => row.Language, row => row.Body);

        foreach (var (language, _, bodySource) in Translations(Version2))
        {
            var withNone = _renderer.Render(bodySource, Model(), language, encodeHtml: true).Value;
            var before = _renderer.Render(version1[language], Model(), language, encodeHtml: true).Value;

            Regex.Replace(withNone, @"\s+", " ").Should().Be(Regex.Replace(before, @"\s+", " "),
                $"an account without a passkey gets the {language} notice it always got");
        }
    }

    [Fact]
    public void ADatabaseThatHoldsTheType_GainsBothVariables_ByAnIdempotentUpdate()
    {
        // S24's way (template 24's Purpose): the guarded insert carries the new catalog for a
        // fresh database, and an update gives it to one that already holds the type.
        var seed = File.ReadAllText(SeedPath(TypesSeed)).Replace("\r\n", "\n");
        var insert = Regex.Match(seed,
            @"'40000000-0000-0000-0000-000000000018',\s+N'password-changed',.*?N'(?<vars>\[\{""name"".*?\])',\s+N'(?<sample>\{.*?\})',",
            RegexOptions.Singleline);
        var update = Regex.Match(seed,
            @"UPDATE \[dbo\]\.\[NotificationTypes\]\s+SET \[VariablesJson\] = N'(?<vars>\[[^']*\])',\s+\[SampleDataJson\] = N'(?<sample>\{[^']*\})'\s+"
            + @"WHERE \[Id\] = '40000000-0000-0000-0000-000000000018'\s+AND \[VariablesJson\] NOT LIKE N'%""name"":""PasskeyCount""%';");

        insert.Success.Should().BeTrue();
        update.Success.Should().BeTrue("the guarded insert never reaches a database that already holds the type");
        update.Groups["vars"].Value.Should().Be(insert.Groups["vars"].Value, "the update writes the catalog a fresh database gets");
        update.Groups["sample"].Value.Should().Be(insert.Groups["sample"].Value);
        insert.Groups["vars"].Value.Should().Contain("\"name\":\"PasskeyCount\"").And.Contain("\"name\":\"Passkeys\"");
        insert.Groups["sample"].Value.Should().EndWith("\"PasskeyCount\":0,\"Passkeys\":[]}");
        update.Index.Should().BeLessThan(Regex.Match(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Index, "inside the seed's single batch");
        NotificationTypeCodes.PasswordChanged.Should().Be("password-changed");
    }

    private static IReadOnlyDictionary<string, object?> Model(params (string Name, string CreatedAt, string? LastUsedAt)[] passkeys) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UserName"] = "Jane Doe",
            ["ChangedAt"] = "2026-11-05 09:14:00Z",
            ["ManageSecurityLink"] = "https://example.com/profile?tab=security",
            ["PasskeyCount"] = passkeys.Length,
            ["Passkeys"] = passkeys
                .Select(passkey => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["Name"] = passkey.Name,
                    ["CreatedAt"] = passkey.CreatedAt,
                    ["LastUsedAt"] = passkey.LastUsedAt,
                })
                .ToList(),
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" },
        };

    private static List<(string Language, string Subject, string Body)> Translations(string versionId)
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-000\d-0018-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
                RegexOptions.Singleline)
            .Select(row => (
                row.Groups["lang"].Value,
                row.Groups["subject"].Value.Replace("''", "'"),
                row.Groups["body"].Value.Replace("''", "'")))
            .ToList();

        rows.Should().HaveCount(Languages.Supported.Count, "one translation per supported language");
        return rows;
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", fileName);
}
