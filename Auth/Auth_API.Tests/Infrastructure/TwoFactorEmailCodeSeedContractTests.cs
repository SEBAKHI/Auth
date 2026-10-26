using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Notifications;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// S24: pins the two-factor-email-code seed (type 0024, the next free id after X12's
/// 0023, one template in seven languages) to what the sender passes, and renders every
/// translation through the real renderer — nothing at deploy time renders a seed.
/// </summary>
public class TwoFactorEmailCodeSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string TypeId = "40000000-0000-0000-0000-000000000024";
    private const string TemplateId = "42000000-0000-0000-0000-000000000024";
    private const string Version1 = "43000000-0000-0000-0000-000000000024";
    private const string Version2 = "43000000-0000-0000-0002-000000000024";
    private const string Code = "482913";

    // What EmailCodeSender.SendAsync passes, no more.
    private static readonly string[] Catalog = ["UserName", "OtpCode", "ExpirationMinutes", "RequestedAt", "IpAddress", "Purpose"];

    /// <summary>Every seeded version: version 2 is published over version 1, and both must render.</summary>
    public static TheoryData<string> Versions => new() { Version1, Version2 };

    private readonly FluidTemplateRenderer _renderer = new();

    [Fact]
    public void TheType_IsTheNextFreeId_ASystemType_InsideTheSeedsSingleBatch()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var ids = Regex.Matches(seed, @"IF NOT EXISTS \(SELECT 1 FROM \[dbo\]\.\[NotificationTypes\] WHERE \[Id\] = '40000000-0000-0000-0000-0000000000(\d\d)'")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

        ids.Should().Contain(24, "the next free id after X12's 0023");
        // OI-215: its three types take the next free ids after this one.
        ids.Max().Should().Be(27, "OI-215's three types take 0025 to 0027");
        ids.Should().OnlyHaveUniqueItems();

        var block = TypeBlock(seed);
        block.Should().Contain($"N'{NotificationTypeCodes.TwoFactorEmailCode}'");
        NotificationTypeCodes.TwoFactorEmailCode.Should().Be("two-factor-email-code");
        block.Should().MatchRegex(@"N'[^']*(?:''[^']*)*',\s*1,\s*N'\[", "IsSystem = 1");

        var goes = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline);
        goes.Should().HaveCount(1, "10_ is one batch by design");
        seed.IndexOf(TypeId, StringComparison.Ordinal).Should().BeLessThan(goes.Single().Index);
    }

    [Fact]
    public void TheType_IsListedAsSystem_Sensitive_AndDeliveryRequired()
    {
        NotificationTypeCodes.SystemCodes.Should().Contain(NotificationTypeCodes.TwoFactorEmailCode);
        NotificationTypeCodes.SensitiveContentCodes.Should().Contain(NotificationTypeCodes.TwoFactorEmailCode,
            "its body carries the live code, so it is redacted once sent or dead");
        NotificationTypeCodes.DeliveryRequiredCodes.Should().Contain(NotificationTypeCodes.TwoFactorEmailCode);
    }

    [Fact]
    public void TheCatalog_IsWhatTheSenderPasses_AndTheSampleHoldsIt()
    {
        var block = TypeBlock(File.ReadAllText(SeedPath(TypesSeed)));
        var variablesJson = Regex.Match(block, @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value.Replace("''", "'");

        var variables = JsonDocument.Parse(variablesJson).RootElement.EnumerateArray()
            .ToDictionary(v => v.GetProperty("name").GetString()!, v => v.GetProperty("required").GetBoolean());

        variables.Keys.Should().BeEquivalentTo(Catalog);
        variables.Where(v => !v.Value).Select(v => v.Key).Should().Equal(["IpAddress"], "only the client address may be unknown");

        var sampleJson = Regex.Match(block, @"N'(\{""[^']*(?:''[^']*)*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");
        JsonDocument.Parse(sampleJson).RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(Catalog);

        // The sender's own dictionary, read from its source: a renamed variable on either side fails here.
        var sender = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(),
            "Auth.Application", "Features", "Authentication", "Common", "EmailCodeSender.cs"));
        Regex.Matches(sender, @"\[""(\w+)""\] = ").Select(m => m.Groups[1].Value).Should().BeEquivalentTo(Catalog);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryTranslation_RendersWithNothingUnresolved_ForEveryPurpose_AndNoSubjectCarriesTheCode(string versionId)
    {
        var translations = Translations(versionId);
        translations.Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported);

        foreach (var (language, subjectSource, bodySource) in translations)
        {
            subjectSource.Should().NotContain("OtpCode", $"the {language} subject is shown in notification previews and inbox lists");
            bodySource.Should().NotContain("href=", $"the {language} message is read, never clicked");

            foreach (var purpose in TwoFactorEmailCodePurposes.All)
            {
                var subject = _renderer.RenderTracking(subjectSource, Model(purpose), language, encodeHtml: false, out var subjectMissing);
                var body = _renderer.RenderTracking(bodySource, Model(purpose), language, encodeHtml: true, out var bodyMissing);

                subject.IsError.Should().BeFalse();
                body.IsError.Should().BeFalse($"the {language} body must render for {purpose}");
                subjectMissing.Should().BeEmpty();
                bodyMissing.Should().BeEmpty($"the {language} body names nothing the sender does not pass");
                subject.Value.Should().NotBeNullOrWhiteSpace().And.NotContain("{%");
                // 37 appears nowhere else in the model, so only the lifetime can print it.
                body.Value.Should().Contain(Code).And.Contain("203.0.113.7").And.Contain("37")
                    .And.NotContain("{{").And.NotContain("{%");
            }
        }
    }

    /// <summary>
    /// Review of #107 (F2): a code a signed-in session asked for — to switch the emailed
    /// code on, or off or over to an app — must never read as a sign-in code: the owner
    /// would be told to enter it "on the sign-in page", the page a phishing proxy clones.
    /// Version 2 branches on the purpose in every translation; a sign-in keeps version
    /// 1's text exactly.
    /// </summary>
    [Fact]
    public void Version2_WordsEachPurpose_AndASignInCodeKeepsVersion1sTextExactly()
    {
        var version1 = Translations(Version1).ToDictionary(row => row.Language);

        foreach (var (language, subjectSource, bodySource) in Translations(Version2))
        {
            subjectSource.Should().Contain("{% case Purpose %}", $"the {language} subject names the purpose");
            Regex.Matches(bodySource, @"\{% case Purpose %\}").Should().HaveCount(3,
                $"the {language} heading, lead and if-this-was-not-you text branch on the purpose");

            string Render(string source, string purpose, bool html) =>
                _renderer.Render(source, Model(purpose), language, encodeHtml: html).Value;

            var signInSubject = Render(version1[language].Subject, TwoFactorEmailCodePurposes.SignIn, false);
            var signInBody = Render(version1[language].Body, TwoFactorEmailCodePurposes.SignIn, true);
            Render(subjectSource, TwoFactorEmailCodePurposes.SignIn, false).Should().Be(signInSubject);
            Render(bodySource, TwoFactorEmailCodePurposes.SignIn, true).Should().Be(signInBody,
                $"a {language} sign-in code reads as it always did");

            // The sentence that makes it a sign-in code: someone entered the password to
            // sign in. Its first clause alone must appear nowhere else.
            var signInSentence = FirstSentence(Lead(signInBody));
            var signInWarning = Warning(signInBody);
            foreach (var purpose in new[] { TwoFactorEmailCodePurposes.Enrollment, TwoFactorEmailCodePurposes.SwitchOff })
            {
                var body = Render(bodySource, purpose, true);
                body.Should().NotContain(signInSentence, $"a {language} {purpose} code was not asked for by a sign-in");
                Warning(body).Should().NotBe(signInWarning,
                    $"the {language} {purpose} warning says someone signed in asked for it, not that someone holds the password");
                Render(subjectSource, purpose, false).Should().NotBe(signInSubject);
            }

            Render(bodySource, TwoFactorEmailCodePurposes.DeletionRecovery, true).Should().NotContain(signInSentence,
                $"a {language} recovery code is not a sign-in code either");
        }
    }

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
    }

    [Fact]
    public void ADatabaseThatHoldsTheType_GainsThePurposeVariable_ByAnIdempotentUpdate()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var insertCatalog = Regex.Match(TypeBlock(seed), @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value;

        var update = Regex.Match(seed,
            @"UPDATE \[dbo\]\.\[NotificationTypes\]\s+SET \[VariablesJson\] = N'(?<vars>\[.*?\])',\s+\[SampleDataJson\] = N'(?<sample>\{.*?\})'\s+"
            + @"WHERE \[Id\] = '" + TypeId + @"'\s+AND \[VariablesJson\] NOT LIKE N'%""name"":""Purpose""%';",
            RegexOptions.Singleline);

        update.Success.Should().BeTrue("the guarded insert never reaches a database that already holds the type");
        update.Groups["vars"].Value.Should().Be(insertCatalog, "the update writes the catalog a fresh database gets");
        update.Index.Should().BeLessThan(Regex.Match(seed, @"^\s*GO\s*$", RegexOptions.Multiline).Index, "inside the seed's single batch");
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void WithoutAClientAddress_TheFromLineIsLeftOut(string versionId)
    {
        var model = new Dictionary<string, object?>(Model()) { ["IpAddress"] = null };

        foreach (var (language, _, bodySource) in Translations(versionId))
        {
            _renderer.Render(bodySource, model, language, encodeHtml: true).Value
                .Should().NotContain("203.0.113.7").And.NotContain("<br />", $"the {language} details line ends at the time");
        }
    }

    [Fact]
    public void BothSeedFiles_KeepTheirByteOrderMark()
    {
        foreach (var file in new[] { TypesSeed, TemplatesSeed })
        {
            File.ReadAllBytes(SeedPath(file)).Take(3).Should().Equal(Encoding.UTF8.GetPreamble(),
                $"{file} carries seven languages and is read as UTF-8 by its BOM");
        }
    }

    private static IReadOnlyDictionary<string, object?> Model(string purpose = TwoFactorEmailCodePurposes.SignIn) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UserName"] = "Jane Doe",
            ["OtpCode"] = Code,
            ["ExpirationMinutes"] = 37,
            ["RequestedAt"] = "2026-10-23 09:14:00Z",
            ["IpAddress"] = "203.0.113.7",
            ["Purpose"] = purpose,
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" },
        };

    /// <summary>The paragraph after the greeting: what the code is for.</summary>
    private static string Lead(string body) =>
        Regex.Matches(body, "<p class=\"message\">(.*?)</p>", RegexOptions.Singleline)[1].Groups[1].Value;

    /// <summary>A paragraph's first sentence, up to its first full stop in any of the seven scripts.</summary>
    private static string FirstSentence(string paragraph)
    {
        var sentence = Regex.Match(paragraph, "^[^.。۔]+").Value.Trim();
        sentence.Length.Should().BeGreaterThan(20, "the sign-in lead starts with a whole sentence");
        return sentence;
    }

    /// <summary>The if-this-was-not-you text: the last notice.</summary>
    private static string Warning(string body) =>
        Regex.Matches(body, "<p class=\"notice-text\">(.*?)</p>", RegexOptions.Singleline)[^1].Groups[1].Value;

    private static List<(string Language, string Subject, string Body)> Translations(string versionId = Version1)
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-000\d-0024-0000000000\d\d', '{versionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
                RegexOptions.Singleline)
            .Select(row => (
                row.Groups["lang"].Value,
                row.Groups["subject"].Value.Replace("''", "'"),
                row.Groups["body"].Value.Replace("''", "'")))
            .ToList();

        rows.Should().HaveCount(Languages.Supported.Count, "one translation per supported language");
        return rows;
    }

    private static string TypeBlock(string seed)
    {
        var start = seed.IndexOf($"IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '{TypeId}')", StringComparison.Ordinal);
        start.Should().BePositive($"{TypeId} needs a guarded insert");
        var end = seed.IndexOf("\nEND", start, StringComparison.Ordinal);
        return seed[start..end];
    }

    private static string SeedPath(string fileName) =>
        Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", fileName);
}

/// <summary>The emailed second factor's signal codes: pinned, well formed, unique.</summary>
public class TwoFactorSignalCodesTests
{
    private static readonly Regex Format = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$", RegexOptions.Compiled);

    [Fact]
    public void Codes_ArePinned()
    {
        TwoFactorSignalCodes.EmailCodeUnavailable.Should().Be("two-factor.email-code-unavailable");
        TwoFactorSignalCodes.EmailCodeThrottled.Should().Be("two-factor.email-code-throttled");
        TwoFactorSignalCodes.ProviderMailboxWaived.Should().Be("two-factor.provider-mailbox-waived");
        TwoFactorSignalCodes.SignInCodeMailboxWaived.Should().Be("two-factor.sign-in-code-mailbox-waived");
    }

    [Fact]
    public void All_ListsEveryConstant_EachWellFormedAndUnique_AndNoneCollidesWithTheLockSignals()
    {
        var constants = typeof(TwoFactorSignalCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().HaveCount(4);
        TwoFactorSignalCodes.All.Should().BeEquivalentTo(constants);
        constants.Should().OnlyHaveUniqueItems().And.AllSatisfy(code => Format.IsMatch(code).Should().BeTrue(code));
        constants.Should().NotIntersectWith(AccountLockSignalCodes.All);
    }

    [Fact]
    public void TheSender_WritesTheCodes_AsTheEventCodeOfItsLines()
    {
        var sender = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(),
            "Auth.Application", "Features", "Authentication", "Common", "EmailCodeSender.cs"));

        sender.Should().Contain("TwoFactorSignalCodes.EmailCodeUnavailable").And.Contain("TwoFactorSignalCodes.EmailCodeThrottled");
        Regex.Matches(sender, @"""\{EventCode\}:").Should().HaveCount(3, "every refusal line names its signal, never as literal text");
        sender.Should().NotContain("\"two-factor.email-code", "the code is written through the constant");
    }

    [Fact]
    public void TheProviderWaiver_WritesItsCode_AsTheEventCodeOfItsLine()
    {
        var handler = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(),
            "Auth.Application", "Features", "Authentication", "ExternalLogin", "ExternalLoginCommandHandler.cs"));

        handler.Should().Contain("\"{EventCode}: no second step").And.Contain("TwoFactorSignalCodes.ProviderMailboxWaived");
        handler.Should().NotContain("\"two-factor.provider-mailbox-waived", "the code is written through the constant");
    }
}

/// <summary>
/// LK4 (4), extended by S24: an administrator may not remove a factor whose emailed code
/// reached the limit until the owner's password moved after that failure.
/// </summary>
public class EmailCodeHardLockGateTests
{
    private static TwoFactorSnapshot Row(int emailFailures, DateTime lockedAt) =>
        new(Guid.NewGuid(), null, "[\"h\"]", isEnabled: false, failedAttempts: 0, lockedUntil: null,
            modifiedAt: lockedAt, emailCodeEnabledAt: lockedAt.AddDays(-30), emailFailedAttempts: emailFailures);

    [Fact]
    public void AHardLockedEmailCode_WithoutAPasswordChangeAfterIt_KeepsTheResetClosed()
    {
        var lockedAt = DateTime.UtcNow.AddHours(-1);

        Row(100, lockedAt).EnsureAdministratorMayReset(100, passwordChangedAt: lockedAt.AddHours(-2))
            .FirstError.Code.Should().Be("TwoFactor.PasswordResetRequiredFirst");
        Row(100, lockedAt).EnsureAdministratorMayReset(100, passwordChangedAt: null)
            .FirstError.Code.Should().Be("TwoFactor.PasswordResetRequiredFirst");
    }

    [Fact]
    public void APasswordChangedAfterTheLock_OrACountBelowIt_OpensTheReset()
    {
        var lockedAt = DateTime.UtcNow.AddHours(-1);

        Row(100, lockedAt).EnsureAdministratorMayReset(100, passwordChangedAt: lockedAt.AddMinutes(5)).IsError.Should().BeFalse();
        Row(99, lockedAt).EnsureAdministratorMayReset(100, passwordChangedAt: null).IsError.Should().BeFalse();
    }
}
