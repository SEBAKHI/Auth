using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the two-factor-changed seed (P3 deploy-1 batch) to the code that will send it, and
/// proves that its Liquid logic renders.
///
/// It is the first seeded template that branches: one type for every kind of change, with the
/// heading and the explanation chosen by <c>{% case ChangeKind %}</c> and the device line printed
/// by <c>{% if %}</c>. Nothing at deploy time renders a seed: the version is inserted with its
/// publish pointer already set and never passes the console's publish gate, and the preview
/// renders the sample data, which holds one kind. A misspelt <c>when</c> value or an unclosed
/// tag would surface as an empty heading in a security notice, in production, for the kinds
/// nobody previewed. So every kind is rendered here, in every language, through the real
/// renderer.
/// </summary>
public class TwoFactorChangedSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string TypeId = "40000000-0000-0000-0000-000000000021";
    private const string VersionId = "43000000-0000-0000-0000-000000000021";

    /// <summary>The kinds this version words, each with a word its English heading must carry.</summary>
    public static TheoryData<string, string> Kinds => new()
    {
        { "enabled", "turned on" },
        { "disabled", "turned off" },
        { "recovery-codes-regenerated", "recovery codes" },
        { "authenticator-replaced", "authenticator app" },
        { "reset-by-administrator", "administrator" },
    };

    private static readonly string[] Catalog =
        ["UserName", "ChangeKind", "OccurredAtUtc", "DeviceName", "ManageSecurityLink"];

    private readonly FluidTemplateRenderer _renderer = new();

    [Fact]
    public void TheType_IsASystemType_SeededInsideTheSeedsSingleBatch()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));

        TypeBlock(seed, TypeId).Should().Contain($"N'{NotificationTypeCodes.TwoFactorChanged}'");
        TypeBlock(seed, TypeId).Should().MatchRegex(@"N'[^']*',\s*1,\s*N'\[",
            "IsSystem = 1 is what the template guards read: a system template cannot be unpublished or deleted");
        NotificationTypeCodes.SystemCodes.Should().Contain(NotificationTypeCodes.TwoFactorChanged);

        // 10_ declares @SystemUserId once; a block after its only GO is Msg 137 at publish time.
        var goes = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline);
        goes.Should().HaveCount(1, "10_ is one batch by design");
        seed.IndexOf(TypeId, StringComparison.Ordinal).Should().BeLessThan(goes.Single().Index);
    }

    [Fact]
    public void TheCatalog_IsTheFiveVariables_WithOnlyDeviceNameOptional()
    {
        var block = TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), TypeId);
        var variablesJson = Regex.Match(block, @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        var variables = JsonDocument.Parse(variablesJson).RootElement.EnumerateArray()
            .ToDictionary(
                variable => variable.GetProperty("name").GetString()!,
                variable => variable.GetProperty("required").GetBoolean());

        variables.Keys.Should().BeEquivalentTo(Catalog,
            "the catalog is the contract the X02 sender fills; a variable in one and not the other renders blank");
        variables.Where(pair => !pair.Value).Select(pair => pair.Key).Should().Equal(["DeviceName"],
            "an administrator's reset has no device of the owner's to name; everything else is always known");

        // The publish gate and the console preview render the sample data, so it must hold
        // every variable the template names, the optional one included.
        SampleModel(block).Keys.Should().BeEquivalentTo(Catalog);
    }

    [Fact]
    public void EveryTranslation_UsesOnlyCatalogedVariables()
    {
        foreach (var (language, subject, body) in Translations())
        {
            var used = Regex.Matches(subject + body, @"\{\{\s*([A-Za-z_][\w.]*)\s*\}\}|\{%-?\s*(?:case|if)\s+([A-Za-z_]\w*)")
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .Where(name => !name.StartsWith("Platform.", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            used.Should().BeSubsetOf(Catalog, $"the {language} translation names only cataloged variables");
            used.Should().Contain(["ChangeKind", "OccurredAtUtc", "ManageSecurityLink", "UserName"],
                $"the {language} translation says what changed, when, to whom, and where to review it");
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryKind_RendersInEveryLanguage_WithNothingUnresolved(string kind, string englishHeadingWord)
    {
        var translations = Translations();
        translations.Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported);

        foreach (var (language, subjectSource, bodySource) in translations)
        {
            var model = Model(kind, deviceName: "Chrome on Windows");
            var subject = _renderer.RenderTracking(subjectSource, model, language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, model, language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse($"the {language} subject must render: {(subject.IsError ? subject.FirstError.Description : "")}");
            body.IsError.Should().BeFalse($"the {language} body must render: {(body.IsError ? body.FirstError.Description : "")}");
            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} body names nothing the sender will not pass");

            body.Value.Should().NotContain("{{").And.NotContain("{%").And.NotContain("%}");
            Heading(body.Value).Should().NotBeNullOrWhiteSpace($"the {language} heading for {kind} must not be empty");
            body.Value.Should().Contain("Example Platform")
                .And.Contain("2026-09-19 09:14:00Z")
                .And.Contain("Chrome on Windows")
                .And.Contain("href=\"https://example.com/profile?tab=security\"");

            if (language == "en")
            {
                Heading(body.Value).Should().Contain(englishHeadingWord, $"the {kind} heading names the change");
            }
        }
    }

    [Fact]
    public void EachKind_HasItsOwnWording_InEveryLanguage_AndAnUnknownKindFallsBackToGenericWording()
    {
        var kinds = Kinds.Select(row => (string)row[0]).Append("a-kind-added-later").ToList();

        foreach (var (language, _, bodySource) in Translations())
        {
            var bodies = kinds
                .Select(kind => _renderer.Render(bodySource, Model(kind, "Chrome on Windows"), language, encodeHtml: true).Value)
                .ToList();

            bodies.Select(Heading).Should().OnlyHaveUniqueItems(
                $"each {language} heading must say which change happened; a copy-pasted branch tells the owner the wrong thing");
            bodies.Select(Explanation).Should().OnlyHaveUniqueItems($"each {language} explanation must differ per kind");
            bodies.Select(Heading).Should().AllSatisfy(heading => heading.Should().NotBeNullOrWhiteSpace(
                $"a kind this version does not know must fall to the {language} else-wording, not to an empty heading"));
        }
    }

    [Fact]
    public void TheDeviceLine_IsPrintedOnlyWhenDeviceNameHasAValue()
    {
        foreach (var (language, _, bodySource) in Translations())
        {
            var withDevice = _renderer.Render(bodySource, Model("disabled", "Chrome on Windows"), language, encodeHtml: true).Value;
            var withNull = _renderer.Render(bodySource, Model("disabled", null), language, encodeHtml: true).Value;
            var withEmpty = _renderer.Render(bodySource, Model("disabled", ""), language, encodeHtml: true).Value;
            var absent = Model("disabled", null).Where(pair => pair.Key != "DeviceName").ToDictionary();
            var withoutKey = _renderer.RenderTracking(bodySource, absent, language, encodeHtml: true, out var missing).Value;

            Notice(withDevice).Should().Contain("<br />").And.Contain("Chrome on Windows",
                $"with a device the {language} notice names it on its own line");
            foreach (var body in new[] { withNull, withEmpty, withoutKey })
            {
                Notice(body).Should().NotContain("<br />",
                    $"without a device the {language} notice must not print a device label with nothing after it");
                Notice(body).Should().Contain("2026-09-19 09:14:00Z");
            }

            missing.Should().Equal(["DeviceName"], "DeviceName is the only variable a sender may leave out");
        }
    }

    private static IReadOnlyDictionary<string, object?> Model(string kind, string? deviceName) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UserName"] = "Jane Doe",
            ["ChangeKind"] = kind,
            ["OccurredAtUtc"] = "2026-09-19 09:14:00Z",
            ["DeviceName"] = deviceName,
            ["ManageSecurityLink"] = "https://example.com/profile?tab=security",
            // Injected by the rendering service into every render.
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" },
        };

    private static string Heading(string body) =>
        Regex.Match(body, "<h1>(.*?)</h1>", RegexOptions.Singleline).Groups[1].Value.Trim();

    /// <summary>The paragraph after the greeting: the one that explains the change.</summary>
    private static string Explanation(string body) =>
        Regex.Matches(body, "<p class=\"message\">(.*?)</p>", RegexOptions.Singleline)[1].Groups[1].Value;

    private static string Notice(string body) =>
        Regex.Match(body, "<p class=\"notice-text\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;

    /// <summary>Language, subject source and body source of every translation of version 0021.</summary>
    private static List<(string Language, string Subject, string Body)> Translations()
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-0000-0021-0000000000\d\d', '{VersionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
                RegexOptions.Singleline)
            .Select(row => (
                row.Groups["lang"].Value,
                row.Groups["subject"].Value.Replace("''", "'"),
                row.Groups["body"].Value.Replace("''", "'")))
            .ToList();

        rows.Should().HaveCount(Languages.Supported.Count, "one translation per supported language");
        return rows;
    }

    /// <summary>The seeded sample data, the model the console preview and the publish gate render.</summary>
    private static IReadOnlyDictionary<string, object?> SampleModel(string typeBlock)
    {
        var sampleJson = Regex.Match(typeBlock, @"N'(\{""[^']*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        return JsonDocument.Parse(sampleJson).RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.GetString(), StringComparer.Ordinal);
    }

    /// <summary>The guarded INSERT block for one type id, from its IF NOT EXISTS to its END.</summary>
    private static string TypeBlock(string seed, string typeId)
    {
        var start = seed.IndexOf($"IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '{typeId}')", StringComparison.Ordinal);
        start.Should().BePositive($"{typeId} needs a guarded insert");
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
