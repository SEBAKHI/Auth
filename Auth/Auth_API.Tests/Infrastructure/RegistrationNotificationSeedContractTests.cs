using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Pins the seed data behind the two verify-first registration messages to the
/// constants that will send them, and the variable catalog each template draws
/// on to the variables the handler will actually pass.
///
/// The second half is the part the platform does not check on its own. The
/// publish gate (UnknownVariables) runs when an admin publishes from the
/// console; a seeded version is inserted with its publish pointer already set
/// and never passes through it. A template that names a variable the handler
/// does not pass renders an empty string in every language, silently, in
/// production — and for the code template that would be a message with no code.
///
/// The batch-placement assertion is the 2026-07-28 incident: a seed block after
/// the file's terminating GO lands in a batch of its own with no @SystemUserId,
/// fails to compile (Msg 137), and aborts the publish AFTER the schema changed.
/// </summary>
public class RegistrationNotificationSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";

    /// <summary>Code, seeded type id, id ordinal, and the catalog the handler will pass.</summary>
    public static TheoryData<string, string, string, string[]> RegistrationTypes => new()
    {
        {
            NotificationTypeCodes.RegistrationVerification,
            "40000000-0000-0000-0000-000000000019",
            "0019",
            ["OtpCode", "ExpirationMinutes"]
        },
        {
            NotificationTypeCodes.RegistrationAttemptExistingAccount,
            "40000000-0000-0000-0000-000000000020",
            "0020",
            ["SignInLink", "ResetPasswordLink", "AttemptedAt"]
        },
    };

    [Theory]
    [MemberData(nameof(RegistrationTypes))]
    public void EachRegistrationType_IsSeededWithItsCode_AsASystemType(string code, string typeId, string _, string[] __)
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));

        seed.Should().Contain(typeId, $"{code} needs a NotificationTypes row");
        seed.Should().Contain($"N'{code}'",
            "calling code resolves the type by code, so the seeded code must match the constant");

        // IsSystem is the column the template guards read (unpublish/delete
        // refuse it); SystemCodes is what the startup check reports on. Both.
        TypeBlock(seed, typeId).Should().MatchRegex(@"N'[^']*',\s*1,\s*N'\[",
            $"{code} must be seeded with IsSystem = 1 — the flag the unpublish and delete guards read");
        NotificationTypeCodes.SystemCodes.Should().Contain(code,
            "the startup check reports a missing published template only for system codes");
    }

    [Theory]
    [MemberData(nameof(RegistrationTypes))]
    public void EachRegistrationType_CatalogsExactlyWhatTheHandlerPasses(string code, string typeId, string _, string[] catalog)
    {
        var block = TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), typeId);
        var variablesJson = Regex.Match(block, @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        var names = JsonDocument.Parse(variablesJson).RootElement
            .EnumerateArray()
            .Select(variable => variable.GetProperty("name").GetString())
            .ToList();

        names.Should().BeEquivalentTo(catalog,
            $"the {code} catalog is the contract between the handler and the template; " +
            "a variable in one and not the other renders as an empty string, silently");
        names.Should().NotContain("UserName",
            "no Users row exists when the code goes out, and a reserved address has nobody to greet: " +
            "neither message may require a name");
    }

    [Theory]
    [MemberData(nameof(RegistrationTypes))]
    public void EachRegistrationTemplate_ShipsSevenTranslationsAndIsPublished(
        string code, string typeId, string ordinal, string[] _)
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var templateId = $"42000000-0000-0000-0000-0000000000{ordinal[2..]}";
        var versionId = $"43000000-0000-0000-0000-0000000000{ordinal[2..]}";

        seed.Should().Contain($"VALUES ('{templateId}', '{typeId}', NULL, 1, N'en'",
            $"{code} needs a global Email template bound to its type");
        seed.Should().Contain(versionId, $"{code} needs a version row");

        // One translation row per supported language, matched on the id ladder
        // rather than on the language codes alone, so a copy-paste that reuses
        // another template's version id cannot pass by accident.
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
    [MemberData(nameof(RegistrationTypes))]
    public void EveryTranslation_UsesOnlyCatalogedVariables_AndTheCodeTemplateAlwaysShowsTheCode(
        string code, string _, string ordinal, string[] catalog)
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var versionId = $"43000000-0000-0000-0000-0000000000{ordinal[2..]}";

        // Each translation row: id, version, language, subject, body.
        var rows = Regex.Matches(seed,
            $@"'44000000-0000-0000-{ordinal}-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
            RegexOptions.Singleline);
        rows.Should().HaveCount(Languages.Supported.Count);

        foreach (System.Text.RegularExpressions.Match row in rows)
        {
            var language = row.Groups["lang"].Value;
            var text = row.Groups["subject"].Value + row.Groups["body"].Value;

            var used = Regex.Matches(text, @"\{\{\s*([A-Za-z_][\w.]*)\s*\}\}")
                .Select(match => match.Groups[1].Value)
                .Distinct()
                .ToList();

            used.Where(name => !name.StartsWith("Platform.", StringComparison.Ordinal))
                .Should().BeSubsetOf(catalog,
                    $"the {language} translation of {code} names a variable the handler does not pass; " +
                    "the renderer would print an empty string there in production");

            if (code == NotificationTypeCodes.RegistrationVerification)
            {
                used.Should().Contain("OtpCode", $"the {language} code message must contain the code");
                used.Should().Contain("ExpirationMinutes", $"the {language} code message must say when the code dies");
            }
            else
            {
                used.Should().Contain("SignInLink", $"the {language} notice must offer the ordinary sign-in page");
                used.Should().Contain("ResetPasswordLink", $"the {language} notice must offer the ordinary forgotten-password page");
                text.Should().NotContain("{{ OtpCode", "the notice never carries a code — that is its whole point");
            }
        }
    }

    [Theory]
    [MemberData(nameof(RegistrationTypes))]
    public void EveryTranslation_RendersWithTheSeededSampleData_LeavingNothingUnresolved(
        string code, string typeId, string ordinal, string[] _)
    {
        // The consuming medium, as far as a unit test can reach it: the real
        // Fluid renderer, the seeded sample data, the renderer's Platform
        // context. Liquid that parses but references a name the model lacks
        // renders blank; this is where that shows.
        var renderer = new FluidTemplateRenderer();
        var model = SampleModel(TypeBlock(File.ReadAllText(SeedPath(TypesSeed)), typeId));
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var versionId = $"43000000-0000-0000-0000-0000000000{ordinal[2..]}";
        var rows = Regex.Matches(seed,
            $@"'44000000-0000-0000-{ordinal}-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
            RegexOptions.Singleline);
        rows.Should().HaveCount(Languages.Supported.Count);

        foreach (System.Text.RegularExpressions.Match row in rows)
        {
            var language = row.Groups["lang"].Value;
            var subject = renderer.RenderTracking(Unescape(row.Groups["subject"].Value), model, language, encodeHtml: false, out var subjectMissing);
            var body = renderer.RenderTracking(Unescape(row.Groups["body"].Value), model, language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse($"the {language} subject of {code} must parse: {(subject.IsError ? subject.FirstError.Description : "")}");
            body.IsError.Should().BeFalse($"the {language} body of {code} must parse: {(body.IsError ? body.FirstError.Description : "")}");
            subjectMissing.Should().BeEmpty($"the {language} subject of {code} names a variable the sample data does not carry");
            bodyMissing.Should().BeEmpty($"the {language} body of {code} names a variable the sample data does not carry");
            body.Value.Should().NotContain("{{").And.NotContain("}}");
            body.Value.Should().Contain("Example Platform", "the renderer's Platform.Name context must reach the copy");

            if (code == NotificationTypeCodes.RegistrationVerification)
            {
                body.Value.Should().Contain("<div class=\"otp-code\">123456</div>", $"the {language} message must show the sample code");
            }
            else
            {
                body.Value.Should().Contain("href=\"https://example.com/login\"", $"the {language} notice must link the sign-in page");
                body.Value.Should().Contain("href=\"https://example.com/forgot-password\"", $"the {language} notice must link the forgotten-password page");
                body.Value.Should().NotContain("123456", "the notice never carries a code");
            }
        }
    }

    [Fact]
    public void TheRegistrationCode_IsSensitive_AndTheNoticeIsNot()
    {
        // The delivery-log read model and the dispatcher's at-rest redaction
        // both consult this one set. A code an admin could read out of a
        // pending outbox row is a code that signs a stranger up as someone else.
        NotificationTypeCodes.SensitiveContentCodes.Should().Contain(NotificationTypeCodes.RegistrationVerification,
            "the rendered body carries a live code, like every other OTP type");
        NotificationTypeCodes.SensitiveContentCodes.Should().NotContain(NotificationTypeCodes.RegistrationAttemptExistingAccount,
            "the notice carries no secret, and an admin must be able to see what an owner was told");
    }

    [Fact]
    public void TheTypeBlocks_LiveInsideTheSeedsSingleBatch_BeforeItsFinalGo()
    {
        // 10_ declares @SystemUserId once, at the top, and every block in the
        // file uses it. A block pasted after the terminating GO is compiled as
        // a batch of its own, where the variable does not exist: Msg 137, and
        // under :on error exit the publish stops there — after the DDL ran.
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var lastGo = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Last().Index;

        foreach (var typeId in RegistrationTypes.Select(row => (string)row[1]))
        {
            seed.IndexOf(typeId, StringComparison.Ordinal).Should().BePositive();
            seed.IndexOf(typeId, StringComparison.Ordinal).Should().BeLessThan(lastGo,
                $"the block for {typeId} must sit inside the batch that declares @SystemUserId");
        }

        Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Should().HaveCount(1,
            "10_ is one batch by design; a second GO means a block was appended outside it");
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

    /// <summary>
    /// The render model the seeded sample data describes, plus the Platform
    /// context the rendering service injects into every render.
    /// </summary>
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

    /// <summary>Undoes T-SQL literal escaping: a doubled quote is one quote.</summary>
    private static string Unescape(string literal) => literal.Replace("''", "'");

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
