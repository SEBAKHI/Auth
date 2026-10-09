using Auth.Domain.Constants;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Domain.ValueObjects;

/// <summary>
/// The platform appearance accepts shadcn's vocabulary and nothing else: a
/// stored value can select a palette, never carry one.
/// </summary>
public class PlatformThemeTests
{
    private static ThemeColorChoice Preset(string name) => new(name);

    private static ThemeColorChoice Custom(string? light, string? dark) => new(ThemePresets.Custom, light, dark);

    private static ErrorOr.ErrorOr<PlatformTheme> Create(
        ThemeColorChoice? baseColor = null,
        ThemeColorChoice? theme = null,
        ThemeColorChoice? chart = null,
        string? radius = "default",
        string? menuAccent = "subtle") =>
        PlatformTheme.Create(
            baseColor ?? Preset("neutral"),
            theme ?? Preset("neutral"),
            chart ?? Preset("cyan"),
            radius,
            menuAccent);

    [Fact]
    public void Default_IsWhatTheShippedPresetRenders()
    {
        PlatformTheme.Default.Base.Preset.Should().Be("neutral");
        PlatformTheme.Default.Theme.Preset.Should().Be("neutral");
        PlatformTheme.Default.Chart.Preset.Should().Be("cyan");
        PlatformTheme.Default.Radius.Should().Be("default");
        PlatformTheme.Default.MenuAccent.Should().Be("subtle");
        Create().Value.Should().Be(PlatformTheme.Default);
    }

    [Theory]
    [InlineData("stone", "stone")]
    [InlineData("stone", "amber")]
    [InlineData("mauve", "violet")]
    [InlineData("taupe", "taupe")]
    public void Create_ABaseWithItselfOrAColourfulTheme_Succeeds(string baseColor, string theme)
    {
        var result = Create(Preset(baseColor), Preset(theme), Preset(theme));

        result.IsError.Should().BeFalse();
        result.Value.Theme.Preset.Should().Be(theme);
    }

    [Fact]
    public void Create_AMonochromeThemeOfAnotherBase_IsRefused()
    {
        // shadcn's picker never offers Zinc on a Stone base.
        var result = Create(Preset("stone"), Preset("zinc"));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ThemeColorUnavailableForBase");
    }

    [Fact]
    public void Create_AMonochromeThemeOnACustomBase_IsRefused()
    {
        var result = Create(Custom("#112233", "#445566"), Preset("neutral"), Preset("blue"));

        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Code == "SystemSettings.ThemeColorUnavailableForBase");
    }

    [Theory]
    [InlineData("blue")]       // a colourful theme is not a base colour
    [InlineData("Neutral")]    // names are exact
    [InlineData("red;}body{")] // nothing that is not a name
    public void Create_AnUnknownBaseColour_IsRefused(string name)
    {
        var result = Create(Preset(name));

        result.Errors.Should().Contain(e => e.Code == "SystemSettings.ThemeUnknownColor");
    }

    [Theory]
    [InlineData("#AABBCC", "#ddeeff")]
    [InlineData("#000000", "#FFFFFF")]
    public void Create_ACustomChoice_StoresBothColoursInLowerCase(string light, string dark)
    {
        var result = Create(theme: Custom(light, dark));

        result.IsError.Should().BeFalse();
        result.Value.Theme.Should().Be(new ThemeColorChoice("custom", light.ToLowerInvariant(), dark.ToLowerInvariant()));
    }

    [Theory]
    [InlineData(null, "#ffffff")]
    [InlineData("#ffffff", null)]
    [InlineData("#fff", "#ffffff")]
    [InlineData("ffffff", "#ffffff")]
    [InlineData("#ffffff", "red")]
    [InlineData("#ffffff", "#ffffff;}")]
    [InlineData("#ffffff", "oklch(0.5 0.1 20)")]
    public void Create_ACustomChoiceWithoutTwoHexColours_IsRefused(string? light, string? dark)
    {
        var result = Create(chart: Custom(light, dark));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ThemeCustomColorInvalid");
    }

    [Fact]
    public void Create_ColoursSentWithARegistryName_AreDropped()
    {
        var result = Create(theme: new ThemeColorChoice("blue", "#112233", "#445566"));

        result.Value.Theme.Should().Be(new ThemeColorChoice("blue"));
    }

    [Fact]
    public void Create_AMissingChoice_IsRefusedByName()
    {
        var result = PlatformTheme.Create(Preset("neutral"), null, Preset("cyan"), "default", "subtle");

        result.FirstError.Code.Should().Be("SystemSettings.ThemeChoiceRequired");
        result.FirstError.Metadata!["args"].Should().BeEquivalentTo(new object[] { "theme" });
    }

    [Theory]
    [InlineData("huge", "subtle", "SystemSettings.ThemeUnknownRadius")]
    [InlineData(null, "subtle", "SystemSettings.ThemeUnknownRadius")]
    [InlineData("small", "loud", "SystemSettings.ThemeUnknownMenuAccent")]
    [InlineData("small", null, "SystemSettings.ThemeUnknownMenuAccent")]
    public void Create_AnUnknownRadiusOrMenuAccent_IsRefused(string? radius, string? menuAccent, string code)
    {
        var result = Create(radius: radius, menuAccent: menuAccent);

        result.FirstError.Code.Should().Be(code);
    }

    [Fact]
    public void Create_ReportsEveryProblemAtOnce()
    {
        var result = PlatformTheme.Create(null, Custom("#fff", null), Preset("nope"), "huge", "loud");

        result.Errors.Select(e => e.Code).Should().BeEquivalentTo(
        [
            "SystemSettings.ThemeChoiceRequired",
            "SystemSettings.ThemeCustomColorInvalid",
            "SystemSettings.ThemeUnknownColor",
            "SystemSettings.ThemeUnknownRadius",
            "SystemSettings.ThemeUnknownMenuAccent",
        ]);
    }

    [Fact]
    public void IsAvailableForBase_MatchesShadcnsPicker()
    {
        ThemePresets.IsAvailableForBase("olive", "olive").Should().BeTrue();
        ThemePresets.IsAvailableForBase("teal", "olive").Should().BeTrue();
        ThemePresets.IsAvailableForBase("mist", "olive").Should().BeFalse();
        ThemePresets.IsAvailableForBase("custom", "custom").Should().BeFalse();
    }
}
