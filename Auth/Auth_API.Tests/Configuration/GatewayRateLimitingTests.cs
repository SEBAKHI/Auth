using System.Text.Json;
using Auth.Application.Features.SystemSettings.Common;
using Auth.Application.SystemSettings;
using ErrorOr;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// The gateway's limiter is console-owned but lives in a second process, so its
/// numbers exist in three places: this registry section, the API's file layer
/// that feeds the settings pull, and the gateway's own file layer that keeps it
/// running while the API is unreachable. Three copies of one number is a drift
/// waiting to happen, and drift here is silent — the console would report a
/// limit the edge is not applying.
/// </summary>
public class GatewayRateLimitingTests
{
    /// <summary>Every field the section is allowed to expose, in order.</summary>
    private static readonly string[] ExpectedFields =
    [
        "GlobalPermitLimit",
        "GlobalWindowSeconds",
        "GlobalQueueLimit",
        "AuthPermitLimit",
        "AuthWindowSeconds",
        "ApiPermitLimit",
        "ApiWindowSeconds",
        "AdminPermitLimit",
        "AdminWindowSeconds"
    ];

    private static SettingSectionDefinition Section()
    {
        var section = SystemSettingsRegistry.TryGet("GatewayRateLimiting");
        section.Should().NotBeNull("the console cannot own the gateway's limits without this section");
        return section!;
    }

    #region Registry shape

    [Fact]
    public void Section_ExposesExactlyTheNineGatewayLimits()
    {
        var section = Section();

        section.Fields.Select(f => f.Path).Should().BeEquivalentTo(
            ExpectedFields,
            options => options.WithStrictOrdering());

        section.Editable.Should().BeTrue();
        section.Group.Should().Be(SettingGroups.Access);
        section.Fields.Should().OnlyContain(f => f.Kind == SettingKind.Int);
        section.Fields.Should().OnlyContain(f => f.Editable);

        // The gateway applies a saved value within one poll interval, so no
        // field may claim a restart is needed — the badge would be a lie, and
        // an operator would restart the edge for nothing.
        section.Fields.Should().OnlyContain(f => !f.RestartRequired);
    }

    [Fact]
    public void Section_DoesNotShareItsConfigRootWithTheApisOwnLimits()
    {
        // Both sections would otherwise flatten onto "RateLimiting:*", and the
        // provider would have two stored rows writing into one config subtree.
        Section().ConfigRoot.Should().Be("GatewayRateLimiting");
        SystemSettingsRegistry.TryGet("RateLimiting")!.ConfigRoot.Should().Be("RateLimiting");
    }

    [Fact]
    public void GlobalQueueLimit_AllowsZeroForNoQueue()
    {
        // Zero is a real answer here ("reject on arrival"), unlike the permit
        // counts where zero would silence the edge. A Min of 1 would make
        // "no queue" unreachable.
        var field = SystemSettingsRegistry.TryGetField(Section(), "GlobalQueueLimit");

        field!.Min.Should().Be(0);
        field.DefaultValue.Should().Be(100);
    }

    [Fact]
    public void PermitAndWindowFields_CannotBeSetToZero()
    {
        // A permit limit or window of 0 rejects every request that reaches the
        // gateway. The console must not be able to produce that state.
        foreach (var field in Section().Fields.Where(f => f.Path != "GlobalQueueLimit"))
        {
            field.Min.Should().BeGreaterThan(0, "{0} would take the edge down at 0", field.Path);
        }
    }

    [Fact]
    public void AdminPermitLimit_IsNotStricterThanTheGeneralApiLimit()
    {
        // The regression this section was written for: /admin/** shipped at 10
        // requests a minute — below the general api policy — and a console
        // screen spends several requests per action, so an administrator was
        // throttled out of ordinary work. Authorization and the audit log
        // defend these routes; the throttle is not what makes them safe.
        var section = Section();
        var admin = SystemSettingsRegistry.TryGetField(section, "AdminPermitLimit")!;
        var api = SystemSettingsRegistry.TryGetField(section, "ApiPermitLimit")!;

        Convert.ToInt64(admin.DefaultValue).Should().BeGreaterThanOrEqualTo(
            Convert.ToInt64(api.DefaultValue));
    }

    #endregion

    #region Cross-field rule

    [Fact]
    public void Save_IsRefused_WhenAPolicyWouldOutrunTheGlobalBucket()
    {
        // Every request passes the global limiter first, so a policy raised
        // above it changes nothing — the control would report success and do
        // nothing at all.
        var errors = Validate(
            payload: [("ApiPermitLimit", 2000)],
            effective: Defaults());

        errors.Should().ContainSingle()
            .Which.Description.Should().Contain("ApiPermitLimit");
    }

    [Fact]
    public void Save_ReportsTheErrorOnTheFieldTheAdministratorEdited()
    {
        // Lowering the global ceiling undercuts all three policies at once, but
        // that is one mistake and deserves one message — pointed at the control
        // just touched, not at the three it happens to cap.
        var errors = Validate(
            payload: [("GlobalPermitLimit", 10)],
            effective: Defaults());

        errors.Should().ContainSingle()
            .Which.Description.Should().Contain("GlobalPermitLimit");
    }

    [Fact]
    public void Save_ComparesRatesRatherThanRawPermitCounts()
    {
        // 100 per 3600s is far slower than 100 per 60s. Comparing the permit
        // numbers alone would call this pair equal and let it through.
        var effective = Defaults();
        effective["GatewayRateLimiting:GlobalWindowSeconds"] = "3600";

        var errors = Validate(payload: [], effective: effective);

        errors.Should().NotBeEmpty("a slow global window still caps every policy under it");
        errors.Should().HaveCount(3, "nothing was edited, so each capped policy is flagged on its own field");
    }

    [Fact]
    public void Save_IsAccepted_WhenTheGlobalBucketStaysTheFastest()
    {
        Validate(payload: [("AdminPermitLimit", 500)], effective: Defaults())
            .Should().BeEmpty();
    }

    [Fact]
    public void Save_StaysSilent_WhenAValueIsUnreadable()
    {
        // Shape errors are the per-field validation's to report; adding a
        // second, vaguer error for the same keystroke helps nobody.
        var effective = Defaults();
        effective["GatewayRateLimiting:GlobalPermitLimit"] = "not-a-number";

        Validate(payload: [], effective: effective).Should().BeEmpty();
    }

    #endregion

    #region Helpers

    /// <summary>The registry's own defaults, as the effective configuration.</summary>
    private static Dictionary<string, string?> Defaults()
    {
        var section = Section();
        return section.Fields.ToDictionary(
            field => section.FullKey(field),
            field => (string?)Convert.ToString(field.DefaultValue),
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<Error> Validate(
        (string Path, long Value)[] payload,
        Dictionary<string, string?> effective)
    {
        var section = Section();
        var errors = new List<Error>();

        // The payload wins over the stored value for the fields it carries —
        // the rule has to judge the configuration this save WOULD produce.
        var values = payload
            .Select(entry => new KeyValuePair<string, JsonElement>(
                entry.Path,
                JsonDocument.Parse(entry.Value.ToString()).RootElement.Clone()))
            .ToList();

        SystemSettingsValueValidator.ValidateSectionRules(
            section,
            values,
            errors,
            key => effective.TryGetValue(key, out var value) ? value : null);

        return errors;
    }

    #endregion
}
