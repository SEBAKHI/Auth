using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;

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

    /// <summary>S20: version 2 says where the code goes for an app or a passkey.</summary>
    private const string Version2Id = "43000000-0000-0000-0002-000000000022";

    /// <summary>Every seeded version: the published one is the newest, so each must render.</summary>
    public static TheoryData<string> Versions => new() { VersionId, Version2Id };

    /// <summary>What FirstFactorEmailProof passes, and whether a sender may leave it out.</summary>
    private static readonly Dictionary<string, bool> Catalog = new(StringComparer.Ordinal)
    {
        ["UserName"] = true,
        ["OtpCode"] = true,
        ["ExpirationMinutes"] = true,
        ["RequestedAt"] = true,
        ["DeviceName"] = false,
        ["IpAddress"] = false,
        // REVIEW 150 F11: which factor the code finishes adding; both senders pass it.
        ["Factor"] = true,
    };

    private readonly FluidTemplateRenderer _renderer = new(TimeProvider.System);

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

    /// <summary>
    /// REVIEW-FIXES 150 F17: the guarded insert gives Factor to a fresh database, and only this
    /// update gives it to one that already holds the type — so the update is pinned like its
    /// siblings: the catalog and the sample a fresh database gets, written once.
    /// </summary>
    /// <remarks>Deliberate break, verified red: Factor dropped from the update's catalog.</remarks>
    [Fact]
    public void ADatabaseThatHoldsTheType_GainsFactor_ByAnIdempotentUpdate()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed)).Replace("\r\n", "\n");
        var insert = Regex.Match(seed,
            @"'40000000-0000-0000-0000-000000000022',\s+N'two-factor-bind-code',.*?N'(?<vars>\[\{""name"".*?\])',\s+N'(?<sample>\{.*?\})',",
            RegexOptions.Singleline);
        var update = Regex.Match(seed,
            @"UPDATE \[dbo\]\.\[NotificationTypes\]\s+SET \[VariablesJson\] = N'(?<vars>\[[^']*\])',\s+\[SampleDataJson\] = N'(?<sample>\{[^']*\})'\s+"
            + @"WHERE \[Id\] = '40000000-0000-0000-0000-000000000022'\s+AND \[VariablesJson\] NOT LIKE N'%""name"":""Factor""%';");

        insert.Success.Should().BeTrue();
        update.Success.Should().BeTrue("the guarded insert never reaches a database that already holds the type");
        update.Groups["vars"].Value.Should().Contain(@"{""name"":""Factor""", "the update brings the variable")
            .And.Be(insert.Groups["vars"].Value, "the update writes the catalog a fresh database gets");
        update.Groups["sample"].Value.Should().Contain(@"""Factor"":")
            .And.Be(insert.Groups["sample"].Value, "and its sample, which the preview and the publish gate render");
        update.Index.Should().BeLessThan(Regex.Match(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Index, "inside the seed's single batch");
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
        Translations(VersionId).Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported,
            "the code goes out in the account's own language, whichever it is");
        seed.Should().Contain($"SET [PublishedVersionId] = '{VersionId}'",
            "an unpublished template fails the renderer at send time, not at deploy time");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryTranslation_UsesOnlyCatalogedVariables_ShowsTheCode_AndCarriesNoLink(string versionId)
    {
        foreach (var (language, subject, body) in Translations(versionId))
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

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryTranslation_RendersTheSeededSample_WithNothingUnresolved(string versionId)
    {
        var model = SampleModel();

        foreach (var (language, subjectSource, bodySource) in Translations(versionId))
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

    [Theory]
    [MemberData(nameof(Versions))]
    public void TheDeviceAndAddressLines_ArePrintedOnlyWhenKnown(string versionId)
    {
        foreach (var (language, _, bodySource) in Translations(versionId))
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

    /// <summary>
    /// REVIEW 150 F11 (D-150-1 = (b)): for the authenticator app — and for any other Factor,
    /// or none, as an API older than the variable sends — version 2 renders version 1's words
    /// exactly, subject included, so nothing changes for anyone who binds the app.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the app's lead reworded in version 2.</remarks>
    [Theory]
    [InlineData(BindCodeFactors.AuthenticatorApp)]
    [InlineData(null)]
    public void Version2_ForTheApp_IsVersion1sWordsExactly(string? factor)
    {
        var version1 = Translations(VersionId).ToDictionary(row => row.Language);

        foreach (var (language, subjectSource, bodySource) in Translations(Version2Id))
        {
            var model = ModelFor(factor);
            var before = version1[language];

            Squash(_renderer.Render(subjectSource, model, language, encodeHtml: false).Value)
                .Should().Be(Squash(_renderer.Render(before.Subject, model, language, encodeHtml: false).Value), $"the {language} subject");
            Squash(_renderer.Render(bodySource, model, language, encodeHtml: true).Value)
                .Should().Be(Squash(_renderer.Render(before.Body, model, language, encodeHtml: true).Value), $"the {language} body");
        }
    }

    /// <summary>
    /// REVIEW 150 F11: for a passkey the email says plainly what the code does — the device that
    /// holds the passkey will sign in with its fingerprint, face or PIN, without the password —
    /// and what to do if it was not you, in its subject, heading, lead and "not you" text.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the passkey branch's lead made the app's again.</remarks>
    [Fact]
    public void Version2_ForAPasskey_SaysTheCodeLetsADeviceSignInWithoutThePassword()
    {
        var version1 = Translations(VersionId).ToDictionary(row => row.Language);
        var words = new Dictionary<string, (string Passkey, string Fingerprint, string WithoutPassword)>
        {
            ["en"] = ("passkey", "fingerprint", "without your password"),
            ["ar"] = ("مفتاح مرور", "بصمة الإصبع", "دون كلمة المرور"),
            ["tr"] = ("geçiş anahtarı", "parmak izi", "parolanız olmadan"),
            ["fr"] = ("clé d'accès", "empreinte", "sans votre mot de passe"),
            ["zh"] = ("通行密钥", "指纹", "无需密码"),
            ["ur"] = ("پاس کی", "فنگر پرنٹ", "پاس ورڈ کے بغیر"),
            ["fa"] = ("کلید عبور", "اثر انگشت", "بدون رمز عبور"),
        };

        foreach (var (language, subjectSource, bodySource) in Translations(Version2Id))
        {
            var model = ModelFor(BindCodeFactors.Passkey);
            var subject = _renderer.RenderTracking(subjectSource, model, language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, model, language, encodeHtml: true, out var bodyMissing);
            var before = version1[language];

            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} passkey wording names nothing the sender does not pass");
            body.Value.Should().NotContain("{{").And.NotContain("{%").And.NotContain("href");
            body.Value.Should().Contain("<div class=\"otp-code\">123456</div>");

            var (passkey, fingerprint, withoutPassword) = words[language];
            subject.Value.Should().NotBe(_renderer.Render(before.Subject, model, language, encodeHtml: false).Value,
                $"the {language} subject names the passkey, not two-factor authentication");
            Lead(body.Value).Should().Contain(passkey).And.Contain(fingerprint).And.Contain(withoutPassword,
                $"the {language} lead says the code lets a device sign in without the password");
            NotYou(body.Value).Should().NotBe(NotYou(_renderer.Render(before.Body, model, language, encodeHtml: true).Value),
                $"the {language} \"not you\" text says what the code would give away");
        }

        var english = _renderer.Render(Translations(Version2Id).Single(row => row.Language == "en").Body,
            ModelFor(BindCodeFactors.Passkey), "en", encodeHtml: true).Value;
        english.Should().NotContain("together with the code from your authenticator app")
            .And.NotContain("turn on two-factor authentication");
        NotYou(english).Should().Contain("Do not enter the code").And.Contain("change it now");
    }

    private static IReadOnlyDictionary<string, object?> ModelFor(string? factor)
    {
        var model = new Dictionary<string, object?>(SampleModel(), StringComparer.Ordinal);
        if (factor is null)
        {
            model.Remove("Factor");
        }
        else
        {
            model["Factor"] = factor;
        }

        return model;
    }

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>The paragraph that says when the code expires.</summary>
    private static string Lead(string body) =>
        Regex.Matches(body, "<p class=\"message\">(.*?)</p>", RegexOptions.Singleline)[1].Groups[1].Value;

    /// <summary>The "if this was not you" text: the second notice.</summary>
    private static string NotYou(string body) =>
        Regex.Matches(body, "<p class=\"notice-text\">(.*?)</p>", RegexOptions.Singleline)[1].Groups[1].Value;

    [Fact]
    public void Version2_IsANewVersionPublishedOnlyWhileTheSeededVersion1IsThePublishedOne()
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        var start = seed.IndexOf($"AND NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTemplateVersions] WHERE [Id] = '{Version2Id}')", StringComparison.Ordinal);
        start.Should().BePositive("version 2 is inserted once, guarded by its own id");
        var block = seed[start..seed.IndexOf("\nEND", start, StringComparison.Ordinal)];

        block.Should().Contain("ISNULL(MAX([VersionNumber]), 0) + 1");
        Regex.Replace(block, @"\s+", " ").Should().Contain(
            $"UPDATE [dbo].[NotificationTemplates] SET [PublishedVersionId] = '{Version2Id}' WHERE [Id] = '{TemplateId}' "
            + $"AND [PublishedVersionId] = '{VersionId}';");
    }

    private static List<(string Language, string Subject, string Body)> Translations(string versionId)
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-000\d-0022-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
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
