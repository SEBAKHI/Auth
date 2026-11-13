using System.Text.Json;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.SystemSettings;
using ErrorOr;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS10 (BD8, ARR-0406): in Production, a save may leave the sitekey neither one of
/// Cloudflare's testing sitekeys nor empty while the Turnstile secret is set. Every other
/// environment accepts both: the testing keys are what development runs on.
/// </summary>
/// <remarks>Deliberate break, verified red: the environment ignored (the rules applied everywhere).</remarks>
public class BotDefenseSettingsValidatorTests
{
    private static readonly SettingSectionDefinition Section = SystemSettingsRegistry.TryGet("BotDefense")!;

    private static List<Error> Save(string siteKey, bool production, string? boundSecret = null)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { SiteKey = siteKey }));
        var values = new List<KeyValuePair<string, JsonElement>> { new("SiteKey", document.RootElement.GetProperty("SiteKey").Clone()) };
        var errors = new List<Error>();
        SystemSettingsValueValidator.ValidateSectionRules(
            Section,
            values,
            errors,
            key => key == "BotDefense:TurnstileSecret" ? boundSecret : null,
            isProduction: production);
        return errors;
    }

    [Fact]
    public void TheSection_IsRegistered_WithItsFiveFields()
    {
        Section.Should().NotBeNull();
        Section.Fields.Select(field => field.Path).Should().Equal("Mode", "FailMode", "SiteKey", "SiteverifyTimeoutMs", "TurnstileSecret");
        Section.Fields.Single(field => field.Path == "TurnstileSecret").Sensitive.Should().BeTrue();
    }

    [Fact]
    public void Production_RefusesATestingSitekey()
    {
        Save("1x00000000000000000000AA", production: true).Should().ContainSingle()
            .Which.Code.Should().Be("SystemSettings.InvalidFieldValue");
    }

    [Fact]
    public void Production_RefusesAnEmptySitekey_WhileTheSecretIsSet()
    {
        Save("", production: true, boundSecret: "0x4AAAAAAAsecret").Should().ContainSingle();
    }

    [Fact]
    public void Production_AcceptsAnEmptySitekey_WithoutASecret_AndARealSitekey()
    {
        Save("", production: true).Should().BeEmpty();
        Save("0x4AAAAAAAsitekey", production: true, boundSecret: "0x4AAAAAAAsecret").Should().BeEmpty();
    }

    [Fact]
    public void OutsideProduction_BothAreAccepted()
    {
        Save("1x00000000000000000000AA", production: false).Should().BeEmpty();
        Save("", production: false, boundSecret: "0x4AAAAAAAsecret").Should().BeEmpty();
    }
    private static List<Error> SaveWithMode(string mode, string siteKey, string boundSecret)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { Mode = mode, SiteKey = siteKey }));
        var values = new List<KeyValuePair<string, JsonElement>>
        {
            new("Mode", document.RootElement.GetProperty("Mode").Clone()),
            new("SiteKey", document.RootElement.GetProperty("SiteKey").Clone()),
        };
        var errors = new List<Error>();
        SystemSettingsValueValidator.ValidateSectionRules(
            Section,
            values,
            errors,
            key => key == "BotDefense:TurnstileSecret" ? boundSecret : null,
            isProduction: true);
        return errors;
    }

    [Fact]
    public void Production_AcceptsAnEmptySitekey_WithTheSecretSet_WhileTheModeIsOff()
    {
        // REVIEW 167 F7: the break-glass and the card's reset leave exactly this; refused, the
        // owner's way back was blocked.
        SaveWithMode("Off", "", boundSecret: "0x4AAAAAAAsecret").Should().BeEmpty();
        SaveWithMode("Enforce", "", boundSecret: "0x4AAAAAAAsecret").Should().ContainSingle()
            .Which.Code.Should().Be("SystemSettings.InvalidFieldValue");
        SaveWithMode("Monitor", "", boundSecret: "0x4AAAAAAAsecret").Should().ContainSingle();
    }

}
