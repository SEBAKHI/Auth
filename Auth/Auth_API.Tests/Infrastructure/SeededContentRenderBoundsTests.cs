using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.LoginDesigns.Common;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The bounds refuse only what no legitimate template does: every seeded notification, in every
/// seeded language and wrapped in the seeded layout, and the starter sign-in design in every
/// language, render to exactly the text they rendered to before, and every seeded email fits the
/// 100,000-byte cap with room to spare.
/// </summary>
public class SeededContentRenderBoundsTests(ITestOutputHelper output)
{
    private const string TypesSeed = "10_NotificationTypes.sql";
    private const string TemplatesSeed = "12_NotificationTemplates.sql";
    private const string LayoutsSeed = "11_NotificationLayouts.sql";

    /// <summary>The share of the cap the largest seeded email may use, so a seed edit has room.</summary>
    private const double HeadroomShare = 0.5;

    [Fact]
    public async Task EverySeededEmail_RendersAsBefore_AndFitsTheCap()
    {
        var translations = SeededTranslations();
        translations.Should().HaveCount(26 * Languages.Supported.Count, "26 seeded templates, seven languages each");
        var (layout, layoutStrings) = SeededLayout();
        var bounded = Service(new FluidTemplateRenderer(TimeProvider.System));
        var unbounded = Service(new UnboundedTemplateRenderer());
        var largest = new Dictionary<string, (int Bytes, string Ordinal)>(StringComparer.Ordinal);

        foreach (var translation in translations)
        {
            var request = new NotificationContentRenderRequest
            {
                LanguageCode = translation.Language,
                Subject = translation.Subject,
                BodyHtml = translation.Body,
                Variables = NotificationMapping.ParseSampleData(SampleDataOf(translation.Ordinal)),
                LayoutContentOverride = layout,
                LayoutStringsJsonOverride = layoutStrings,
            };

            var expected = await unbounded.RenderContentAsync(request, CancellationToken.None);
            var actual = await bounded.RenderContentAsync(request, CancellationToken.None);

            var name = $"template {translation.Ordinal} in {translation.Language}";
            expected.IsError.Should().BeFalse($"{name} rendered before");
            actual.IsError.Should().BeFalse($"{name} must still render: {(actual.IsError ? actual.FirstError.Description : "")}");
            actual.Value.Subject.Should().Be(expected.Value.Subject, name);
            actual.Value.BodyHtml.Should().Be(expected.Value.BodyHtml, name);
            actual.Value.BodyText.Should().Be(expected.Value.BodyText, name);

            var bytes = Encoding.UTF8.GetByteCount(actual.Value.BodyHtml);
            if (!largest.TryGetValue(translation.Language, out var current) || bytes > current.Bytes)
            {
                largest[translation.Language] = (bytes, translation.Ordinal);
            }
        }

        foreach (var (language, (bytes, ordinal)) in largest.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{language}: largest seeded email {bytes:N0} bytes (template {ordinal}), {100.0 * bytes / NotificationLimits.MaxRenderedBytes:F1}% of the cap");
            bytes.Should().BeLessThan((int)(NotificationLimits.MaxRenderedBytes * HeadroomShare),
                $"the largest {language} email must leave the cap room to grow");
        }
    }

    [Fact]
    public void TheStarterDesign_RendersAsBefore_InEveryLanguage()
    {
        var audience = new LoginDesignAudience("Example App", "Example Platform");
        var bounded = new LoginDesignRenderer(
            new FluidTemplateRenderer(TimeProvider.System),
            new Mock<IPlatformSettingsRepository>().Object,
            new Mock<IApplicationRepository>().Object);
        var unbounded = new LoginDesignRenderer(
            new UnboundedTemplateRenderer(),
            new Mock<IPlatformSettingsRepository>().Object,
            new Mock<IApplicationRepository>().Object);

        foreach (var language in Languages.Supported)
        {
            var actual = bounded.Render(LoginDesignStarter.Content, LoginDesignStarter.StringsJson, audience, language);
            var expected = unbounded.Render(LoginDesignStarter.Content, LoginDesignStarter.StringsJson, audience, language);

            actual.IsError.Should().BeFalse(language);
            actual.Value.Should().Be(expected.Value, language);
            Encoding.UTF8.GetByteCount(actual.Value).Should().BeLessThan(LoginDesign.MaxRenderedBytes / 10);
        }

        bounded.CheckForSave(LoginDesignStarter.Content, LoginDesignStarter.StringsJson, audience)
            .IsError.Should().BeFalse("the starter passes its own save gate");
    }

    private static NotificationRenderingService Service(ITemplateRenderer renderer) => new(
        new TemplateCache(new MemoryCache(new MemoryCacheOptions())),
        new Mock<INotificationTemplateRepository>().Object,
        new Mock<INotificationLayoutRepository>().Object,
        new Mock<IUserRepository>().Object,
        new Mock<IApplicationRepository>().Object,
        new Mock<IPlatformSettingsRepository>().Object,
        renderer,
        new Mock<IImageUrlComposer>().Object,
        new Mock<IImageStorageService>().Object,
        TestHelpers.CreateOptions(new EmailSettings { SenderName = "Example Platform", FrontendBaseUrl = "https://example.com" }),
        new Mock<ILogger<NotificationRenderingService>>().Object);

    private static List<(string Ordinal, string Language, string Subject, string Body)> SeededTranslations()
    {
        var seed = File.ReadAllText(SeedPath(TemplatesSeed));
        return Regex.Matches(seed,
                @"'44000000-0000-0000-(?<ordinal>\d{4})-0000000000\d\d', '43000000-0000-0000-0000-0000000000\d\d', N'(?<lang>[a-z]{2})', N'(?<subject>(?:[^']|'')*)',\s*N'(?<body>(?:[^']|'')*)'",
                RegexOptions.Singleline)
            .Select(row => (
                row.Groups["ordinal"].Value,
                row.Groups["lang"].Value,
                Unescape(row.Groups["subject"].Value),
                Unescape(row.Groups["body"].Value)))
            .ToList();
    }

    private static (string Content, string Strings) SeededLayout()
    {
        var seed = File.ReadAllText(SeedPath(LayoutsSeed));
        string Declared(string name) => Unescape(Regex.Match(
            seed, $@"DECLARE @{name} NVARCHAR\(MAX\) = N'(?<value>(?:[^']|'')*)';", RegexOptions.Singleline)
            .Groups["value"].Value);

        var content = Declared("LayoutContent");
        content.Should().Contain("{{ content | raw }}", "the seeded layout wraps the body");
        return (content, Declared("LayoutStrings"));
    }

    private static string SampleDataOf(string ordinal)
    {
        var seed = File.ReadAllText(SeedPath(TypesSeed));
        var typeId = $"40000000-0000-0000-0000-0000000000{ordinal[2..]}";
        var start = seed.IndexOf($"IF NOT EXISTS (SELECT 1 FROM [dbo].[NotificationTypes] WHERE [Id] = '{typeId}')", StringComparison.Ordinal);
        start.Should().BePositive($"{typeId} is the type of template {ordinal}");
        var block = seed[start..seed.IndexOf("\nEND", start, StringComparison.Ordinal)];
        var sample = Regex.Match(block, @"N'(?<json>\{(?:[^']|'')*\})',\s*1,\s*GETUTCDATE\(\)", RegexOptions.Singleline);
        sample.Success.Should().BeTrue($"{typeId} is seeded with sample data");
        return Unescape(sample.Groups["json"].Value);
    }

    private static string Unescape(string literal) => literal.Replace("''", "'");

    private static string SeedPath(string fileName) =>
        Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Scripts", "SeedData", fileName);
}
