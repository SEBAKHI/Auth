using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the two-factor-bind-code seed (X02 PR B, type 0022) to the code that sends it:
/// the code an account enters before it binds its FIRST second factor.
/// <para>
/// Nothing at deploy time renders a seed: the version goes in with its publish pointer
/// already set and never passes the console's publish gate. A template naming a variable
/// the sender does not pass renders it as an empty string in production — for this type,
/// a code email with no code, which would make every first factor unbindable while email
/// is on. So every translation is rendered here through the real renderer.
/// </para>
/// </summary>
public class TwoFactorBindCodeSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string TypeId = "40000000-0000-0000-0000-000000000022";
    private const string TemplateId = "42000000-0000-0000-0000-000000000022";
    private const string VersionId = "43000000-0000-0000-0000-000000000022";

    /// <summary>What FirstFactorEmailProof passes, and whether a sender may leave it out.</summary>
    private static readonly Dictionary<string, bool> Catalog = new(StringComparer.Ordinal)
    {
        ["UserName"] = true,
        ["OtpCode"] = true,
        ["ExpirationMinutes"] = true,
        ["RequestedAt"] = true,
        ["DeviceName"] = false,
        ["IpAddress"] = false,
    };

    private readonly FluidTemplateRenderer _renderer = new();

    [Fact]
    public void TheType_IsASystemType_SeededInsideTheSeedsSingleBatch()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));

        TypeBlock(seed).Should().Contain($"N'{NotificationTypeCodes.TwoFactorBindCode}'",
            "the sender resolves the type by its code");
        TypeBlock(seed).Should().MatchRegex(@"N'[^']*',\s*1,\s*N'\[",
            "IsSystem = 1 is what the template guards read: a system template cannot be unpublished or deleted");
        NotificationTypeCodes.SystemCodes.Should().Contain(NotificationTypeCodes.TwoFactorBindCode,
            "without its published template no first factor can be bound while email is on");

        // 10_ declares @SystemUserId once; a block after its only GO is Msg 137 at publish time.
        var goes = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline);
        goes.Should().HaveCount(1, "10_ is one batch by design");
        seed.IndexOf(TypeId, StringComparison.Ordinal).Should().BePositive()
            .And.BeLessThan(goes.Single().Index, "the block must sit inside the batch that declares @SystemUserId");
    }

    [Fact]
    public void TheCatalog_IsWhatTheSenderPasses_WithOnlyDeviceAndAddressOptional()
    {
        var variablesJson = Regex.Match(TypeBlock(File.ReadAllText(SeedPath(TypesSeed))), @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline)
            .Groups[1].Value.Replace("''", "'");

        var declared = JsonDocument.Parse(variablesJson).RootElement.EnumerateArray()
            .ToDictionary(
                variable => variable.GetProperty("name").GetString()!,
                variable => variable.GetProperty("required").GetBoolean(),
                StringComparer.Ordinal);

        declared.Should().BeEquivalentTo(Catalog,
            "the catalog is the contract between the sender and the template; a variable in one and not the other renders empty");
    }

    [Fact]
    public void TheCode_IsSensitive()
    {
        // The delivery log and the outbox's at-rest redaction both consult this set:
        // a code an admin could read out of a pending outbox row binds a factor.
        NotificationTypeCodes.SensitiveContentCodes.Should().Contain(NotificationTypeCodes.TwoFactorBindCode);
    }

    [Fact]
    public void TheTemplate_ShipsSevenTranslations_AndIsPublished()
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));

        seed.Should().Contain($"VALUES ('{TemplateId}', '{TypeId}', NULL, 1, N'en'",
            "a global Email template bound to the type");
        seed.Should().Contain($"VALUES ('{VersionId}', '{TemplateId}', 1,", "a version row");
        Translations().Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported,
            "the code goes out in the account's own language, whichever it is");
        seed.Should().Contain($"SET [PublishedVersionId] = '{VersionId}'",
            "an unpublished template fails the renderer at send time, not at deploy time");
    }

    [Fact]
    public void EveryTranslation_UsesOnlyCatalogedVariables_ShowsTheCode_AndCarriesNoLink()
    {
        foreach (var (language, subject, body) in Translations())
        {
            var used = Regex.Matches(subject + body, @"\{\{\s*([A-Za-z_][\w.]*)\s*\}\}")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();

            used.Where(name => !name.StartsWith("Platform.", StringComparison.Ordinal))
                .Should().BeSubsetOf(Catalog.Keys, $"the {language} translation names a variable the sender does not pass");
            used.Should().Contain("OtpCode", $"the {language} message must contain the code");
            used.Should().Contain("ExpirationMinutes", $"the {language} message must say when the code dies");

            // A code message is read, never clicked: nothing for a mail scanner's
            // prefetch to follow, nothing to phish with.
            body.Should().NotContain("href", $"the {language} message carries no link");
            body.Should().NotContain("http", $"the {language} message carries no address to follow");
        }
    }

    [Fact]
    public void EveryTranslation_RendersTheSeededSample_WithNothingUnresolved()
    {
        var model = SampleModel();

        foreach (var (language, subjectSource, bodySource) in Translations())
        {
            var subject = _renderer.RenderTracking(subjectSource, model, language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, model, language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse($"the {language} subject must parse");
            body.IsError.Should().BeFalse($"the {language} body must parse: {(body.IsError ? body.FirstError.Description : "")}");
            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} body names a variable the sample data does not carry");
            body.Value.Should().NotContain("{{").And.NotContain("}}").And.NotContain("{%");
            body.Value.Should().Contain("<div class=\"otp-code\">123456</div>", $"the {language} message must show the code");
            body.Value.Should().Contain("Example Platform", "the renderer's Platform.Name context must reach the copy");
            body.Value.Should().Contain("Chrome on Windows").And.Contain("203.0.113.7").And.Contain("2026-10-03 09:14:00Z");
        }
    }

    [Fact]
    public void TheDeviceAndAddressLines_ArePrintedOnlyWhenKnown()
    {
        foreach (var (language, _, bodySource) in Translations())
        {
            var withoutBoth = SampleModel()
                .Where(pair => pair.Key is not ("DeviceName" or "IpAddress"))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var empty = new Dictionary<string, object?>(SampleModel(), StringComparer.Ordinal)
            {
                ["DeviceName"] = "",
                ["IpAddress"] = null,
            };

            foreach (var model in new IReadOnlyDictionary<string, object?>[] { withoutBoth, empty })
            {
                var body = _renderer.Render(bodySource, model, language, encodeHtml: true).Value;
                var details = Regex.Match(body, "<p class=\"notice-text\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;

                details.Should().Contain("2026-10-03 09:14:00Z", $"the {language} details always say when");
                details.Should().NotContain("<br />",
                    $"without a device or an address the {language} details print no label with nothing after it");
            }
        }
    }

    [Theory]
    [InlineData(TypesSeed)]
    [InlineData(TemplatesSeed)]
    public void SeedFile_KeepsItsByteOrderMark(string fileName)
    {
        var bytes = File.ReadAllBytes(SeedPath(fileName));

        (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeTrue(
            "the seeded copy is Arabic, Chinese, Urdu and Persian, and sqlcmd mangles it without a BOM");
    }

    /// <summary>The seeded sample data, plus the Platform context the renderer injects into every render.</summary>
    private static IReadOnlyDictionary<string, object?> SampleModel()
    {
        var sampleJson = Regex.Match(TypeBlock(File.ReadAllText(SeedPath(TypesSeed))), @"N'(\{""[^']*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline)
            .Groups[1].Value.Replace("''", "'");

        var model = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in JsonDocument.Parse(sampleJson).RootElement.EnumerateObject())
        {
            model[property.Name] = property.Value.ValueKind == JsonValueKind.Number
                ? property.Value.GetInt32()
                : property.Value.GetString();
        }

        model.Keys.Should().BeEquivalentTo(Catalog.Keys, "the sample data exercises every variable");
        model["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" };
        return model;
    }

    /// <summary>Language, subject source and body source of every translation of version 0022.</summary>
    private static List<(string Language, string Subject, string Body)> Translations()
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-0000-0022-0000000000\d\d', '{VersionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
                RegexOptions.Singleline)
            .Select(row => (
                row.Groups["lang"].Value,
                row.Groups["subject"].Value.Replace("''", "'"),
                row.Groups["body"].Value.Replace("''", "'")))
            .ToList();

        rows.Should().HaveCount(Languages.Supported.Count, "one translation per supported language");
        return rows;
    }

    /// <summary>The guarded INSERT block for the type, from its IF NOT EXISTS to its END.</summary>
    private static string TypeBlock(string seed)
    {
        var start = seed.IndexOf($"IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '{TypeId}')", StringComparison.Ordinal);
        start.Should().BePositive($"{TypeId} needs a guarded insert");
        var end = seed.IndexOf("\nEND", start, StringComparison.Ordinal);
        return seed[start..end];
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", fileName);

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
