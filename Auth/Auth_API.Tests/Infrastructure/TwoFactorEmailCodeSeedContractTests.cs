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
    private const string VersionId = "43000000-0000-0000-0000-000000000024";
    private const string Code = "482913";

    // What EmailCodeSender.SendAsync passes, no more.
    private static readonly string[] Catalog = ["UserName", "OtpCode", "ExpirationMinutes", "RequestedAt", "IpAddress"];

    private readonly FluidTemplateRenderer _renderer = new();

    [Fact]
    public void TheType_IsTheNextFreeId_ASystemType_InsideTheSeedsSingleBatch()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var ids = Regex.Matches(seed, @"WHERE \[Id\] = '40000000-0000-0000-0000-0000000000(\d\d)'")
            .Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();

        ids.Max().Should().Be(24, "the next free id after X12's 0023");
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

    [Fact]
    public void EveryTranslation_RendersWithNothingUnresolved_AndNoSubjectCarriesTheCode()
    {
        var translations = Translations();
        translations.Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported);

        foreach (var (language, subjectSource, bodySource) in translations)
        {
            subjectSource.Should().NotContain("OtpCode", $"the {language} subject is shown in notification previews and inbox lists");
            bodySource.Should().NotContain("href=", $"the {language} message is read, never clicked");

            var subject = _renderer.RenderTracking(subjectSource, Model(), language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, Model(), language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse();
            body.IsError.Should().BeFalse($"the {language} body must render");
            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} body names nothing the sender does not pass");
            body.Value.Should().Contain(Code).And.Contain("203.0.113.7").And.Contain("10")
                .And.NotContain("{{").And.NotContain("{%");
        }
    }

    [Fact]
    public void WithoutAClientAddress_TheFromLineIsLeftOut()
    {
        var model = new Dictionary<string, object?>(Model()) { ["IpAddress"] = null };

        foreach (var (language, _, bodySource) in Translations())
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

    private static IReadOnlyDictionary<string, object?> Model() =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["UserName"] = "Jane Doe",
            ["OtpCode"] = Code,
            ["ExpirationMinutes"] = 10,
            ["RequestedAt"] = "2026-10-23 09:14:00Z",
            ["IpAddress"] = "203.0.113.7",
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" },
        };

    private static List<(string Language, string Subject, string Body)> Translations()
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-0000-0024-0000000000\d\d', '{VersionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
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
    }

    [Fact]
    public void All_ListsEveryConstant_EachWellFormedAndUnique_AndNoneCollidesWithTheLockSignals()
    {
        var constants = typeof(TwoFactorSignalCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.Should().HaveCount(2);
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
