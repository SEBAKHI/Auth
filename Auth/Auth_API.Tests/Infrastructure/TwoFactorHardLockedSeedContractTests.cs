using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Constants;
using Auth.Infrastructure.Notifications;
using Auth_API.Modules.Authentication.EventHandlers;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// X12 PR B: pins the two-factor-hard-locked seed (type 0023, one template in seven
/// languages) to the handler that sends it, and renders both audiences through the
/// real renderer — nothing at deploy time renders a seed, and the console preview
/// renders the sample, which holds one audience.
/// </summary>
/// <remarks>
/// The owner's message names the password reset and the administrator step and
/// carries no console link; an administrator's names the three steps before the
/// reset and links to the account. An Audience the version does not know reads the
/// owner's wording, which says nothing about the administrators.
/// </remarks>
public class TwoFactorHardLockedSeedContractTests
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string TypeId = "40000000-0000-0000-0000-000000000023";
    private const string VersionId = "43000000-0000-0000-0000-000000000023";

    private static readonly string[] Catalog =
        ["UserName", "Audience", "AccountName", "AccountId", "AccountLink", "FailedAttempts", "OccurredAtUtc", "ResetPasswordLink"];

    private readonly FluidTemplateRenderer _renderer = new();

    [Fact]
    public void TheType_IsASystemType_SeededInsideTheSeedsSingleBatch()
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var block = TypeBlock(seed);

        block.Should().Contain($"N'{NotificationTypeCodes.TwoFactorHardLocked}'");
        NotificationTypeCodes.TwoFactorHardLocked.Should().Be("two-factor-hard-locked");
        block.Should().MatchRegex(@"N'[^']*(?:''[^']*)*',\s*1,\s*N'\[",
            "IsSystem = 1: a system template cannot be unpublished or deleted");
        NotificationTypeCodes.SystemCodes.Should().Contain(NotificationTypeCodes.TwoFactorHardLocked);
        NotificationTypeCodes.SensitiveContentCodes.Should().NotContain(NotificationTypeCodes.TwoFactorHardLocked,
            "it carries no secret, so the delivery log may show what was sent");

        var goes = Regex.Matches(seed, @"^\s*GO\s*$", RegexOptions.Multiline);
        goes.Should().HaveCount(1, "10_ is one batch by design");
        seed.IndexOf(TypeId, StringComparison.Ordinal).Should().BeLessThan(goes.Single().Index);
    }

    [Fact]
    public void TheCatalog_IsWhatTheHandlerSends_AllRequired_AndTheSampleHoldsIt()
    {
        var block = TypeBlock(File.ReadAllText(SeedPath(TypesSeed)));
        var variablesJson = Regex.Match(block, @"N'(\[\{""name"".*?\])',", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");

        var variables = JsonDocument.Parse(variablesJson).RootElement.EnumerateArray()
            .ToDictionary(
                variable => variable.GetProperty("name").GetString()!,
                variable => variable.GetProperty("required").GetBoolean());

        variables.Keys.Should().BeEquivalentTo(Catalog);
        variables.Values.Should().OnlyContain(required => required, "every value is always known to the sender");

        var sampleJson = Regex.Match(block, @"N'(\{""[^']*(?:''[^']*)*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline).Groups[1].Value
            .Replace("''", "'");
        JsonDocument.Parse(sampleJson).RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(Catalog,
            "the publish gate and the console preview render the sample");
    }

    [Fact]
    public void EveryTranslation_UsesOnlyCatalogedVariables_AndBranchesOnTheAudienceOnly()
    {
        var translations = Translations();
        translations.Select(row => row.Language).Should().BeEquivalentTo(Languages.Supported);

        foreach (var (language, subject, body) in translations)
        {
            subject.Should().NotContain("{%", $"the {language} subject is the same for both audiences");
            var used = Regex.Matches(subject + body, @"\{\{-?\s*([A-Za-z_][\w.]*)|\{%-?\s*(?:case|if|elsif|unless)\s+([A-Za-z_]\w*)")
                .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
                .Where(name => !name.StartsWith("Platform.", StringComparison.Ordinal))
                .Distinct()
                .ToList();

            used.Should().BeSubsetOf(Catalog, $"the {language} translation names only cataloged variables");
            Regex.Matches(body, @"\{%-?\s*if\s+(\w+)").Select(m => m.Groups[1].Value).Distinct()
                .Should().Equal(["Audience"], $"the {language} body branches on the audience alone");
            Regex.Matches(body, @"Audience\s*==\s*""(\w+)""").Select(m => m.Groups[1].Value).Distinct()
                .Should().Equal([TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator],
                    $"the {language} body tests for the administrator; everything else reads the owner's wording");
        }
    }

    [Theory]
    [InlineData(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Owner)]
    [InlineData(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator)]
    [InlineData("an-audience-added-later")]
    public void EachAudience_RendersInEveryLanguage_WithNothingUnresolved(string audience)
    {
        foreach (var (language, subjectSource, bodySource) in Translations())
        {
            var subject = _renderer.RenderTracking(subjectSource, Model(audience), language, encodeHtml: false, out var subjectMissing);
            var body = _renderer.RenderTracking(bodySource, Model(audience), language, encodeHtml: true, out var bodyMissing);

            subject.IsError.Should().BeFalse($"the {language} subject must render");
            body.IsError.Should().BeFalse($"the {language} body must render: {(body.IsError ? body.FirstError.Description : "")}");
            subjectMissing.Should().BeEmpty();
            bodyMissing.Should().BeEmpty($"the {language} body names nothing the handler will not pass");
            body.Value.Should().NotContain("{{").And.NotContain("{%").And.NotContain("%}");
            Regex.Match(body.Value, "<h1>(.*?)</h1>", RegexOptions.Singleline).Groups[1].Value
                .Should().NotBeNullOrWhiteSpace($"the {language} heading must not be empty");
            body.Value.Should().Contain("100", $"the {language} message says how many wrong codes")
                .And.Contain("2026-10-12 09:14:00Z");

            if (audience == TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator)
            {
                body.Value.Should().Contain("href=\"https://example.com/users/2f0a5c4e-8b1d-4c39-9a57-1e6d3b2c7f10\"")
                    .And.Contain("2f0a5c4e-8b1d-4c39-9a57-1e6d3b2c7f10")
                    .And.Contain("Olivia Owner")
                    .And.NotContain("href=\"https://example.com/forgot-password\"",
                        $"the {language} administrator's button opens the account; the owner resets the password");
            }
            else
            {
                body.Value.Should().Contain("href=\"https://example.com/forgot-password\"")
                    .And.NotContain("/users/", $"the {language} owner's message carries no console link")
                    .And.NotContain("Olivia Owner", "the owner is greeted by name, not shown an account card");
            }
        }
    }

    [Fact]
    public void TheTwoAudiences_ReadDifferently_InEveryLanguage()
    {
        foreach (var (language, _, bodySource) in Translations())
        {
            var owner = _renderer.Render(bodySource, Model(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Owner), language, encodeHtml: true).Value;
            var administrator = _renderer.Render(bodySource, Model(TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator), language, encodeHtml: true).Value;

            Heading(owner).Should().NotBe(Heading(administrator), $"the {language} heading says whose factor is locked");
        }
    }

    private static IReadOnlyDictionary<string, object?> Model(string audience) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            // The owner is greeted by name; an administrator by theirs.
            ["UserName"] = audience == TwoFactorHardLockAdminNotificationEventHandler.Audiences.Administrator ? "Ada Admin" : "Jane Doe",
            ["Audience"] = audience,
            ["AccountName"] = "Olivia Owner",
            ["AccountId"] = "2f0a5c4e-8b1d-4c39-9a57-1e6d3b2c7f10",
            ["AccountLink"] = "https://example.com/users/2f0a5c4e-8b1d-4c39-9a57-1e6d3b2c7f10",
            ["FailedAttempts"] = 100,
            ["OccurredAtUtc"] = "2026-10-12 09:14:00Z",
            ["ResetPasswordLink"] = "https://example.com/forgot-password",
            ["Platform"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Name"] = "Example Platform" },
        };

    private static string Heading(string body) =>
        Regex.Match(body, "<h1>(.*?)</h1>", RegexOptions.Singleline).Groups[1].Value.Trim();

    /// <summary>Language, subject source and body source of every translation of version 0023.</summary>
    private static List<(string Language, string Subject, string Body)> Translations()
    {
        var rows = Regex.Matches(File.ReadAllText(SeedPath(TemplatesSeed)),
                $@"'44000000-0000-0000-0023-0000000000\d\d', '{VersionId}', N'(?<lang>[a-z]{{2}})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
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
