using System.Reflection;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Application.SystemSettings;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Security;
using Auth_API.Common.Passkeys;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK2, PK3, PK19, T-A2, T-A3): passkeys run only when switched on AND configured — an RP
/// ID that is a host name and covers every first-party origin, the schema deployed, and no live
/// passkey bound to another RP ID. Otherwise every passkey route answers 404 before
/// authentication, and the posture says why. Shipped off, in the file layer only, out of the
/// console until PR C.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the live-rows query dropped from <c>PasskeyFeature</c>;
/// <c>Covers</c> made a plain suffix match; the middleware moved after authentication; the
/// default of <c>Enabled</c> set true.
/// </remarks>
public class PasskeySettingsGuardTests
{
    private static readonly string[] BothOrigins = [PasskeyTestDoubles.ConsoleOrigin, PasskeyTestDoubles.AccountsOrigin];

    // ── Shipped off (stop condition 7) ──

    [Fact]
    public void Defaults_AreOff_WithNoRpId()
    {
        var settings = new PasskeySettings();

        settings.Enabled.Should().BeFalse();
        settings.RpId.Should().BeEmpty();
        PasskeySettings.SectionName.Should().Be("Passkeys");
    }

    [Fact]
    public void TheApiSettingsFile_ShipsThemOff()
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "appsettings.json")));
        var section = file.RootElement.GetProperty("Passkeys");

        section.GetProperty("Enabled").GetBoolean().Should().BeFalse();
        section.GetProperty("RpId").GetString().Should().BeEmpty();
    }

    [Fact]
    public void NoTrackedSettingsFile_SwitchesPasskeysOn()
    {
        var files = Directory.EnumerateFiles(ApiSourceScan.SolutionDirectory(), "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        files.Should().NotBeEmpty();
        foreach (var path in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.TryGetProperty("Passkeys", out var section)
                && section.TryGetProperty("Enabled", out var enabled))
            {
                enabled.ValueKind.Should().NotBe(JsonValueKind.True, $"{Path.GetFileName(path)} must not switch passkeys on before PR C");
            }
        }
    }

    [Fact]
    public void TheSettings_StayOutOfTheConsole_UntilPrC()
    {
        SystemSettingsRegistry.Sections.Should().NotBeEmpty();
        SystemSettingsRegistry.Sections.Should().NotContain(section =>
            section.ConfigRoot == PasskeySettings.SectionName || section.Key == PasskeySettings.SectionName);
    }

    // ── The assessment (PK2, PK19) ──

    [Fact]
    public void Assess_SwitchedOn_AndConfigured_RunsAsTheRpIdOverTheExactOrigins()
    {
        var assessment = PasskeyConfiguration.Assess(true, PasskeyTestDoubles.RpId, BothOrigins, schemaPresent: true);

        assessment.IsAvailable.Should().BeTrue();
        assessment.IsConfigured.Should().BeTrue();
        assessment.Problems.Should().BeEmpty();
        assessment.RelyingParty!.RpId.Should().Be(PasskeyTestDoubles.RpId);
        assessment.RelyingParty.Origins.Should().BeEquivalentTo(BothOrigins);
    }

    [Fact]
    public void Assess_SwitchedOff_IsNeverAvailable_ButSaysWhetherItWouldRun()
    {
        var assessment = PasskeyConfiguration.Assess(false, PasskeyTestDoubles.RpId, BothOrigins, schemaPresent: true);

        assessment.IsAvailable.Should().BeFalse();
        assessment.IsConfigured.Should().BeTrue();
    }

    public static TheoryData<string, string, string[], bool, string> Refusals => new()
    {
        { "no RP ID", "", BothOrigins, true, PasskeyConfiguration.NoRpId },
        { "a URL", "https://example.com", BothOrigins, true, "is not a host name" },
        { "an address", "10.0.0.1", ["https://10.0.0.1"], true, "is not a host name" },
        { "a single-app host name (E12)", "console.example.com", BothOrigins, true, "does not cover the first-party origin 'https://accounts.example.com'" },
        { "a look-alike suffix", PasskeyTestDoubles.RpId, ["https://notexample.com"], true, "does not cover" },
        { "another domain", PasskeyTestDoubles.RpId, ["https://console.example.org"], true, "does not cover" },
        { "plain http", PasskeyTestDoubles.RpId, ["http://console.example.com"], true, "is not a bare https origin" },
        { "a path", PasskeyTestDoubles.RpId, ["https://console.example.com/app"], true, "is not a bare https origin" },
        { "no origin", PasskeyTestDoubles.RpId, [], true, PasskeyConfiguration.NoOrigins },
        { "no schema", PasskeyTestDoubles.RpId, BothOrigins, false, PasskeyConfiguration.NoSchema },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Assess_RefusesWhatABrowserWouldRefuse_OrTheDatabaseCannotHold(
        string because, string rpId, string[] origins, bool schemaPresent, string problem)
    {
        var assessment = PasskeyConfiguration.Assess(true, rpId, origins, schemaPresent);

        assessment.IsAvailable.Should().BeFalse(because);
        assessment.IsConfigured.Should().BeFalse(because);
        assessment.RelyingParty.Should().BeNull();
        assessment.Problems.Should().Contain(text => text.Contains(problem), because);
    }

    [Fact]
    public void Assess_LocalhostWithAPort_RunsOnADeveloperMachine()
    {
        PasskeyConfiguration.Assess(true, "localhost", ["https://localhost:5173"], schemaPresent: true)
            .RelyingParty!.Origins.Should().Equal("https://localhost:5173");
    }

    // ── PK3: a new RP ID while live passkeys carry another ──

    [Fact]
    public async Task Feature_WithLivePasskeysOfAnotherRpId_StaysOff_AndSaysWhy()
    {
        var assessment = await PasskeyTestDoubles.Feature(otherRpIdLive: true).AssessAsync(CancellationToken.None);

        assessment.IsAvailable.Should().BeFalse("a new RP ID is refused while live rows carry another (PK3)");
        assessment.IsConfigured.Should().BeFalse();
        assessment.Problems.Should().Equal(PasskeyConfiguration.OtherRpIdLive);
    }

    [Fact]
    public async Task Feature_WithoutThem_Runs()
    {
        (await PasskeyTestDoubles.Feature().AssessAsync(CancellationToken.None)).IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Feature_SwitchedOff_AsksNeitherTheSchemaNorTheRows()
    {
        var rows = PasskeyTestDoubles.OtherRpIdRows(live: true);
        var schema = new Mock<IPasskeySchema>(MockBehavior.Strict);
        var feature = new PasskeyFeature(
            Monitor(new PasskeySettings { Enabled = false, RpId = PasskeyTestDoubles.RpId }),
            Monitor(new IdentityProviderSettings { FirstPartySpaOrigins = BothOrigins }),
            new PasskeyRpIdAtStartup(PasskeyTestDoubles.RpId),
            schema.Object,
            rows.Object);

        (await feature.AssessAsync(CancellationToken.None)).IsAvailable.Should().BeFalse();
        rows.Verify(r => r.HasLiveOfAnotherRpIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "a deployment that never turns passkeys on never asks the database on a request");
    }

    // ── The posture (A124, A125) ──

    [Fact]
    public async Task Posture_SwitchedOff_IsOk_WhateverTheRestSays()
    {
        var posture = await Posture(PasskeyTestDoubles.Feature(enabled: false, rpId: ""), otherRpIdLive: false);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Lines.Should().OnlyContain(line => line.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Posture_SwitchedOnWithoutAnRpId_IsCritical_PasskeyConfiguration()
    {
        var posture = await Posture(PasskeyTestDoubles.Feature(rpId: ""), otherRpIdLive: false);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        var line = posture.Lines.Should().ContainSingle(l => l.Level == LogLevel.Critical).Subject;
        line.Arguments[0].Should().Be(PasskeySignalCodes.Configuration);
        line.Arguments[1].Should().Be(PasskeyConfiguration.NoRpId);
    }

    [Fact]
    public async Task Posture_LivePasskeysOfAnotherRpId_AreCritical_PasskeyRpIdMismatch_EvenSwitchedOff()
    {
        var posture = await Posture(PasskeyTestDoubles.Feature(enabled: false, otherRpIdLive: true), otherRpIdLive: true);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Lines.Should().ContainSingle(l => l.Level == LogLevel.Critical)
            .Which.Arguments[0].Should().Be(PasskeySignalCodes.RpIdMismatch);
    }

    // ── 404 while off, for every route, before authentication (PK19) ──

    [Fact]
    public void EveryPasskeyRoute_IsBehindTheSwitch_WithItsPolicy()
    {
        typeof(PasskeysController).GetCustomAttribute<PasskeysEndpointAttribute>().Should().NotBeNull(
            "the attribute on the controller reaches every action's endpoint metadata");
        typeof(PasskeysController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v{version:apiVersion}/auth/passkeys");

        var routes = typeof(PasskeysController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Http: method.GetCustomAttribute<HttpMethodAttribute>()))
            .Where(route => route.Http is not null)
            .ToDictionary(
                route => $"{route.Http!.HttpMethods.Single()} {route.Http.Template}",
                route => route.Method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);

        // T-A3: today's policies until S14 moves these routes.
        routes.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["POST registration/options"] = "login",
            ["POST registration/email-code"] = "two-factor-email-code",
            ["POST registration/verify"] = "login",
            ["GET "] = "sign-in-page",
            ["PATCH {id:guid}"] = "sign-in-page",
            ["DELETE {id:guid}"] = "login",
        });
    }

    [Fact]
    public void TheSwitch_AnswersBeforeAuthentication_AndTheErrorContractWritesThe404()
    {
        var program = File.ReadAllText(Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_API", "Program.cs"));
        var errorContract = program.IndexOf("app.UseErrorContract();", StringComparison.Ordinal);
        var passkeys = program.IndexOf("app.UseMiddleware<PasskeysAvailabilityMiddleware>();", StringComparison.Ordinal);
        var authentication = program.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);

        errorContract.Should().BePositive();
        passkeys.Should().BeGreaterThan(errorContract, "the status-code pages write the empty 404 as Http.NotFound");
        passkeys.Should().BeLessThan(authentication, "a signed-out caller learns nothing more than a signed-in one");
    }

    [Theory]
    [InlineData(false, false, StatusCodes.Status404NotFound)]
    [InlineData(true, true, StatusCodes.Status200OK)]
    public async Task TheMiddleware_AnswersAPasskeyRoute404_WhilePasskeysDoNotRun(bool enabled, bool nextRuns, int status)
    {
        var context = Request(new PasskeysEndpointAttribute());
        var ran = false;

        await new PasskeysAvailabilityMiddleware(_ => { ran = true; return Task.CompletedTask; })
            .InvokeAsync(context, PasskeyTestDoubles.Feature(enabled: enabled));

        ran.Should().Be(nextRuns);
        context.Response.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task TheMiddleware_LeavesEveryOtherRouteAlone()
    {
        var context = Request();
        var ran = false;

        await new PasskeysAvailabilityMiddleware(_ => { ran = true; return Task.CompletedTask; })
            .InvokeAsync(context, PasskeyTestDoubles.Feature(enabled: false));

        ran.Should().BeTrue();
    }

    private static DefaultHttpContext Request(params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test"));
        return context;
    }

    private static async Task<SecurityPosture> Posture(PasskeyFeature feature, bool otherRpIdLive)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => feature)
            .AddSingleton(PasskeyTestDoubles.Schema())
            .AddScoped(_ => PasskeyTestDoubles.OtherRpIdRows(otherRpIdLive).Object);
        await using var provider = services.BuildServiceProvider();

        return await new PasskeyPostureContributor(provider.GetRequiredService<IServiceScopeFactory>())
            .EvaluateAsync(CancellationToken.None);
    }

    private static Microsoft.Extensions.Options.IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = new Mock<Microsoft.Extensions.Options.IOptionsMonitor<T>>();
        monitor.Setup(m => m.CurrentValue).Returns(value);
        return monitor.Object;
    }
}
