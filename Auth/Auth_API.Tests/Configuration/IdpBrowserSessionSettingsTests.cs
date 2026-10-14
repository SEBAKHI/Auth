using Auth.Application.Configuration;
using Auth.Application.SystemSettings;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// OI-154: the two browser-session settings are in the console, in the
/// IdentityProvider section, with the defaults and ranges the settings class
/// holds, and hot (every consumer reads them through IOptionsSnapshot). The
/// defaults themselves are also held by <see cref="SystemSettingsDefaultParityTests"/>.
/// </summary>
public class IdpBrowserSessionSettingsTests
{
    private static SettingFieldDefinition Field(string path) =>
        SystemSettingsRegistry.Sections.Single(section => section.Key == "IdentityProvider")
            .Fields.Should().ContainSingle(field => field.Path == path).Subject;

    [Theory]
    [InlineData("IdpBrowserSessionLifetimeHours", 1, 24, 10)]
    [InlineData("IdpBrowserSessionIdleMinutes", 0, 1440, 30)]
    public void TheField_IsEditableAndHot_WithItsRangeAndDefault(string path, long min, long max, int defaultValue)
    {
        var field = Field(path);

        field.Kind.Should().Be(SettingKind.Int);
        field.Editable.Should().BeTrue();
        field.RestartRequired.Should().BeFalse("read per request through IOptionsSnapshot, so a save applies at once");
        field.Min.Should().Be(min);
        field.Max.Should().Be(max);
        field.DefaultValue.Should().Be(defaultValue);
    }

    [Fact]
    public void TheSettingsClass_HoldsTheSameDefaults()
    {
        var settings = new IdentityProviderSettings();

        settings.IdpBrowserSessionLifetimeHours.Should().Be(10, "Keycloak's SSO Session Max");
        settings.IdpBrowserSessionIdleMinutes.Should().Be(30, "owner decision D-150-3 (a); Keycloak's SSO Session Idle");
        settings.IdpSessionLifetimeDays.Should().Be(7, "Stay signed in keeps today's lifetime");
    }

    [Fact]
    public void TheStaySignedInLifetime_StaysWhereItWas()
    {
        Field("IdpSessionLifetimeDays").DefaultValue.Should().Be(7);
    }
}
