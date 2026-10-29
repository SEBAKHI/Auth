using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// OI-215 T11: pins the seed data behind the three messages of the sign-in by emailed
/// code — the sign-in code, the notice to an address without an account, and the
/// password-removed notice — to the constants that send them, and each template's
/// variables to what its sender passes. The pattern of
/// <see cref="RegistrationNotificationSeedContractTests"/>: a seeded version never
/// passes the console's publish gate, so this is where a template naming a variable
/// the sender does not pass would show, before it renders blank in production.
/// </summary>
public class EmailSignInNotificationSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";

    /// <summary>Code, seeded type id, id ordinal, and the catalog the sender passes.</summary>
    public static TheoryData<string, string, string, string[]> Types => new()
    {
        {
            NotificationTypeCodes.EmailSignInCode,
            "40000000-0000-0000-0000-000000000025",
            "0025",
            ["UserName", "OtpCode", "ExpirationMinutes"]
        },
        {
            NotificationTypeCodes.SignInNoAccount,
            "40000000-0000-0000-0000-000000000026",
            "0026",
            ["SignInLink", "AttemptedAt"]
        },
        {
            NotificationTypeCodes.PasswordRemoved,
            "40000000-0000-0000-0000-000000000027",
            "0027",
            ["UserName", "Reason", "RemovedAt", "ManageSecurityLink"]
        },
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void EachType_IsSeededWithItsCode_AsASystemType(string code, string typeId, string ordinal, string[] catalog)
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));

        seed.Should().Contain($"N'{code}'", "calling code resolves the type by code");
        TypeBlock(seed, typeId).Should().MatchRegex(@"N'[^']*(?:''[^']*)*',\s*1,\s*N'\[",
            $"{code} must be seeded with IsSystem = 1 — the flag the unpublish and delete guards read");
        NotificationTypeCodes.SystemCodes.Should().Contain(code,
            "the startup check reports a missing published template only for system codes");
        ordinal.Should().HaveLength(4);
        catalog.Should().NotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void EachType_CatalogsExactlyWhatTheSenderPasses(string code, string typeId, string _, string[] catalog)
    {
        var block = TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), typeId);
        var variablesJson = Regex.Match(block, @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        var names = JsonDocument.Parse(variablesJson).RootElement
            .EnumerateArray()
            .Select(variable => variable.GetProperty("name").GetString())
            .ToList();

        names.Should().BeEquivalentTo(catalog,
            $"the {code} catalog is the contract between its sender and its template");
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void EachTemplate_ShipsSevenTranslationsAndIsPublished(string code, string typeId, string ordinal, string[] _)
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var templateId = $"42000000-0000-0000-0000-0000000000{ordinal[2..]}";
        var versionId = $"43000000-0000-0000-0000-0000000000{ordinal[2..]}";

        seed.Should().Contain($"VALUES ('{templateId}', '{typeId}', NULL, 1, N'en'",
            $"{code} needs a global Email template bound to its type");

        var translations = Regex
            .Matches(seed, $@"'44000000-0000-0000-{ordinal}-0000000000(\d\d)', '{versionId}', N'([a-z]{{2}})'")
            .Select(match => match.Groups[2].Value)
            .ToList();

        translations.Should().BeEquivalentTo(Languages.Supported,
            $"{code} must ship in every supported language, not only the default one");
        seed.Should().Contain($"SET [PublishedVersionId] = '{versionId}'",
            $"an unpublished {code} template fails the renderer at send time, not at deploy time");
    }

    [Theory]
    [MemberData(nameof(Types))]
    public void EveryTranslation_RendersWithTheSeededSampleData_LeavingNothingUnresolved(
        string code, string typeId, string ordinal, string[] catalog)
    {
        var renderer = new FluidTemplateRenderer(TimeProvider.System);
        var sample = SampleModel(TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), typeId));

        foreach (var (language, subject, body) in Translations(ordinal))
        {
            var text = subject + body;
            Regex.Matches(text, @"\{\{\s*([A-Za-z_][\w.]*)\s*\}\}")
                .Select(match => match.Groups[1].Value)
                .Where(name => !name.StartsWith("Platform.", StringComparison.Ordinal))
                .Should().BeSubsetOf(catalog, $"the {language} translation of {code} names a variable its sender does not pass");

            foreach (var model in Models(code, sample))
            {
                var renderedSubject = renderer.RenderTracking(subject, model, language, encodeHtml: false, out var subjectMissing);
                var renderedBody = renderer.RenderTracking(body, model, language, encodeHtml: true, out var bodyMissing);

                renderedSubject.IsError.Should().BeFalse($"the {language} subject of {code} must parse");
                renderedBody.IsError.Should().BeFalse($"the {language} body of {code} must parse");
                subjectMissing.Should().BeEmpty();
                bodyMissing.Should().BeEmpty();
                renderedBody.Value.Should().NotContain("{{").And.NotContain("{%").And.Contain("Example Platform");

                if (code == NotificationTypeCodes.EmailSignInCode)
                {
                    renderedBody.Value.Should().Contain("<div class=\"otp-code\">123456</div>", $"the {language} message must show the code");
                    renderedSubject.Value.Should().NotContain("123456", "the subject carries no code");
                    renderedBody.Value.Should().NotContain("href=", "a code message is read, never clicked");
                }
                else
                {
                    renderedBody.Value.Should().NotContain("123456", "a notice never carries a code");
                }

                if (code == NotificationTypeCodes.SignInNoAccount)
                {
                    renderedBody.Value.Should().Contain("href=\"https://example.com/login\"");
                }

                if (code == NotificationTypeCodes.PasswordRemoved)
                {
                    renderedBody.Value.Should().Contain("href=\"https://example.com/profile?tab=security\"");
                }
            }
        }
    }

    [Fact]
    public void ThePasswordRemovedNotice_WordsEachReasonApart()
    {
        // The owner's own removal and the retirement of an unproven password are two
        // different events for the reader; a template that rendered one text for both
        // would tell the owner of an administrator-created account the wrong story.
        var renderer = new FluidTemplateRenderer(TimeProvider.System);
        var sample = SampleModel(TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), "40000000-0000-0000-0000-000000000027"));

        foreach (var (language, _, body) in Translations("0027"))
        {
            var rendered = Models(NotificationTypeCodes.PasswordRemoved, sample)
                .Select(model => renderer.RenderTracking(body, model, language, encodeHtml: true, out _).Value)
                .ToList();

            rendered.Should().HaveCount(2);
            rendered[0].Should().NotBe(rendered[1], $"the {language} notice words the two reasons apart");
        }

        PasswordRemovalReasons.Owner.Should().Be("owner");
        PasswordRemovalReasons.SetBeforeAddressProven.Should().Be("set-before-address-proven",
            "the template's condition names this value");
    }

    [Fact]
    public void TheSignInCode_IsSensitive_AndTheNoticesAreNot()
    {
        NotificationTypeCodes.SensitiveContentCodes.Should().Contain(NotificationTypeCodes.EmailSignInCode,
            "its rendered body carries a live code that signs an account in");
        NotificationTypeCodes.SensitiveContentCodes.Should().NotContain(NotificationTypeCodes.SignInNoAccount);
        NotificationTypeCodes.SensitiveContentCodes.Should().NotContain(NotificationTypeCodes.PasswordRemoved);
    }

    [Fact]
    public void TheTypeBlocks_LiveInsideTheSeedsSingleBatch_BeforeItsFinalGo()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var lastGo = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Last().Index;

        foreach (var typeId in Types.Select(row => (string)row[1]))
        {
            seed.IndexOf(typeId, StringComparison.Ordinal).Should().BePositive().And.BeLessThan(lastGo,
                $"the block for {typeId} must sit inside the batch that declares @SystemUserId");
        }
    }

    [Fact]
    public void TheEmailedFactorSeedComment_SaysTheCodeGoesWithTheChallenge()
    {
        // OI-246: since #108 the emailed second factor's code is sent with the sign-in's
        // challenge; the seed's comment said "only when asked".
        File.ReadAllText(SeedPath(TypesSeed)).Should().NotContain("Sent only when asked, never on its");
    }

    private static IEnumerable<(string Language, string Subject, string Body)> Translations(string ordinal)
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var versionId = $"43000000-0000-0000-0000-0000000000{ordinal[2..]}";
        var rows = Regex.Matches(seed,
            $@"'44000000-0000-0000-{ordinal}-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
            RegexOptions.Singleline);
        rows.Should().HaveCount(Languages.Supported.Count, "one translation per supported language");

        return rows.Select(row => (
            row.Groups["lang"].Value,
            Unescape(row.Groups["subject"].Value),
            Unescape(row.Groups["body"].Value)));
    }

    /// <summary>The models a template is rendered with: the sample, and for the removal notice each reason.</summary>
    private static IEnumerable<IReadOnlyDictionary<string, object?>> Models(
        string code, IReadOnlyDictionary<string, object?> sample)
    {
        if (code != NotificationTypeCodes.PasswordRemoved)
        {
            return [sample];
        }

        return new[] { PasswordRemovalReasons.Owner, PasswordRemovalReasons.SetBeforeAddressProven }
            .Select(reason => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(sample, StringComparer.Ordinal)
            {
                ["Reason"] = reason
            })
            .ToList();
    }

    private static IReadOnlyDictionary<string, object?> SampleModel(string typeBlock)
    {
        var sampleJson = Regex.Match(typeBlock, @"N'(\{""[^']*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");
        var model = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in JsonDocument.Parse(sampleJson).RootElement.EnumerateObject())
        {
            model[property.Name] = property.Value.ValueKind == JsonValueKind.Number
                ? property.Value.GetInt32()
                : property.Value.GetString();
        }

        model["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" };
        return model;
    }

    private static string Unescape(string literal) => literal.Replace("''", "'");

    private static string TypeBlock(string seed, string typeId)
    {
        var start = seed.IndexOf($"IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '{typeId}')", StringComparison.Ordinal);
        start.Should().BePositive($"{typeId} needs a guarded insert");
        var end = seed.IndexOf("\nEND", start, StringComparison.Ordinal);
        return seed[start..end];
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", fileName);
}
