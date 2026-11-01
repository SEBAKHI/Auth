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

    private static ThemeSizeChoice Size(string step) => new(step);

    private static ThemeSizeChoice CustomSize(int? px) => new(ThemePresets.Custom, px);

    private static ErrorOr.ErrorOr<PlatformTheme> Create(
        ThemeColorChoice? baseColor = null,
        ThemeColorChoice? theme = null,
        ThemeColorChoice? chart = null,
        string? radius = "default",
        string? menuAccent = "subtle",
        ThemeSizeChoice? controlSize = null,
        ThemeSizeChoice? tableRowHeight = null) =>
        PlatformTheme.Create(
            baseColor ?? Preset("neutral"),
            theme ?? Preset("neutral"),
            chart ?? Preset("cyan"),
            radius,
            menuAccent,
            controlSize ?? ThemeSizeChoice.Default,
            tableRowHeight ?? ThemeSizeChoice.Default);

    [Fact]
    public void Default_IsWhatTheShippedPresetRenders()
    {
        PlatformTheme.Default.Base.Preset.Should().Be("neutral");
        PlatformTheme.Default.Theme.Preset.Should().Be("neutral");
        PlatformTheme.Default.Chart.Preset.Should().Be("cyan");
        PlatformTheme.Default.Radius.Should().Be("default");
        PlatformTheme.Default.MenuAccent.Should().Be("subtle");
        PlatformTheme.Default.ControlSize.Should().Be(new ThemeSizeChoice("default"));
        PlatformTheme.Default.TableRowHeight.Should().Be(new ThemeSizeChoice("default"));
        // The preset draws controls at h-9 and text rows at p-3 around a 20px line.
        PlatformTheme.Default.ControlHeightPx.Should().Be(36);
        PlatformTheme.Default.TableRowHeightPx.Should().Be(44);
        // Google's header is the default (owner, 2026-11-02).
        PlatformTheme.Default.HeaderIconSize.Should().Be("google");
        Create().Value.Should().Be(PlatformTheme.Default);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("google")]
    [InlineData("large")]
    public void Create_EveryHeaderIconSize_IsKept(string size)
    {
        var result = PlatformTheme.Create(
            Preset("neutral"), Preset("neutral"), Preset("cyan"), "default", "subtle",
            ThemeSizeChoice.Default, ThemeSizeChoice.Default, size);

        result.IsError.Should().BeFalse();
        result.Value.HeaderIconSize.Should().Be(size);
    }

    [Fact]
    public void Create_WithoutAHeaderIconSize_TakesGoogle()
    {
        // An application's appearance and a stored row from before the
        // setting leave it out; neither may be refused for it.
        Create().Value.HeaderIconSize.Should().Be(ThemePresets.DefaultHeaderIconSize);
    }

    [Theory]
    [InlineData("huge")]
    [InlineData("Google")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_AnUnknownHeaderIconSize_IsRefused(string? size)
    {
        var result = PlatformTheme.Create(
            Preset("neutral"), Preset("neutral"), Preset("cyan"), "default", "subtle",
            ThemeSizeChoice.Default, ThemeSizeChoice.Default, size);

        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("SystemSettings.ThemeUnknownHeaderIconSize");
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
        var result = PlatformTheme.Create(
            Preset("neutral"), null, Preset("cyan"), "default", "subtle", ThemeSizeChoice.Default, ThemeSizeChoice.Default);

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
        var result = PlatformTheme.Create(
            null, Custom("#fff", null), Preset("nope"), "huge", "loud", Size("tiny"), CustomSize(500));

        result.Errors.Select(e => e.Code).Should().BeEquivalentTo(
        [
            "SystemSettings.ThemeChoiceRequired",
            "SystemSettings.ThemeCustomColorInvalid",
            "SystemSettings.ThemeUnknownColor",
            "SystemSettings.ThemeUnknownRadius",
            "SystemSettings.ThemeUnknownMenuAccent",
            "SystemSettings.ThemeUnknownSize",
            "SystemSettings.ThemeCustomSizeOutOfRange",
        ]);
    }

    [Theory]
    [InlineData("compact", 32)]
    [InlineData("default", 36)]
    [InlineData("comfortable", 40)]
    public void Create_EveryControlStep_IsAcceptedWithItsHeight(string step, int px)
    {
        var result = Create(controlSize: Size(step));

        result.IsError.Should().BeFalse();
        result.Value.ControlSize.Should().Be(Size(step));
        result.Value.ControlHeightPx.Should().Be(px);
    }

    [Theory]
    [InlineData("compact", 40)]
    [InlineData("default", 44)]
    [InlineData("comfortable", 48)]
    public void Create_EveryRowStep_IsAcceptedWithItsHeight(string step, int px)
    {
        var result = Create(tableRowHeight: Size(step));

        result.IsError.Should().BeFalse();
        result.Value.TableRowHeight.Should().Be(Size(step));
        result.Value.TableRowHeightPx.Should().Be(px);
    }

    [Fact]
    public void Create_EveryPairOfSteps_IsAccepted()
    {
        // The smallest row step is no lower than the tallest control step, so
        // no pair of the two three-step choices can be refused.
        foreach (var control in ThemePresets.Sizes)
        {
            foreach (var row in ThemePresets.Sizes)
            {
                Create(controlSize: Size(control), tableRowHeight: Size(row)).IsError
                    .Should().BeFalse($"{control} controls with {row} rows are valid");
            }
        }
    }

    [Theory]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(56)]
    public void Create_ACustomControlHeightWithinBounds_IsKept(int px)
    {
        var result = Create(controlSize: CustomSize(px), tableRowHeight: CustomSize(72));

        result.IsError.Should().BeFalse();
        result.Value.ControlSize.Should().Be(CustomSize(px));
        result.Value.ControlHeightPx.Should().Be(px);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(72)]
    public void Create_ACustomRowHeightWithinBounds_IsKept(int px)
    {
        var result = Create(controlSize: Size("compact"), tableRowHeight: CustomSize(px));

        result.IsError.Should().BeFalse();
        result.Value.TableRowHeight.Should().Be(CustomSize(px));
        result.Value.TableRowHeightPx.Should().Be(px);
    }

    [Theory]
    [InlineData("controlSize", 27)]
    [InlineData("controlSize", 57)]
    [InlineData("controlSize", null)]
    [InlineData("tableRowHeight", 31)]
    [InlineData("tableRowHeight", 73)]
    [InlineData("tableRowHeight", null)]
    public void Create_ACustomHeightOutOfBounds_IsRefusedWithTheBounds(string slot, int? px)
    {
        var result = slot == "controlSize"
            ? Create(controlSize: CustomSize(px))
            : Create(tableRowHeight: CustomSize(px));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("SystemSettings.ThemeCustomSizeOutOfRange");
        result.FirstError.Metadata!["args"].Should().BeEquivalentTo(
            slot == "controlSize" ? new object[] { slot, 28, 56 } : new object[] { slot, 32, 72 });
    }

    [Theory]
    [InlineData("huge")]
    [InlineData("Default")]
    [InlineData("")]
    public void Create_AnUnknownSizeStep_IsRefused(string step)
    {
        var result = Create(controlSize: Size(step));

        result.FirstError.Code.Should().Be("SystemSettings.ThemeUnknownSize");
    }

    [Fact]
    public void Create_AMissingSize_IsRefusedByName()
    {
        var result = PlatformTheme.Create(
            Preset("neutral"), Preset("neutral"), Preset("cyan"), "default", "subtle", ThemeSizeChoice.Default, null);

        result.FirstError.Code.Should().Be("SystemSettings.ThemeUnknownSize");
        result.FirstError.Metadata!["args"].Should().BeEquivalentTo(new object[] { "tableRowHeight", "" });
    }

    [Fact]
    public void Create_PixelsSentWithAStep_AreDropped()
    {
        var result = Create(controlSize: new ThemeSizeChoice("comfortable", 50));

        result.Value.ControlSize.Should().Be(Size("comfortable"));
        result.Value.ControlHeightPx.Should().Be(40);
    }

    [Theory]
    [InlineData(48, "default", 44)]
    [InlineData(41, "compact", 40)]
    public void Create_ARowStepBelowACustomControlHeight_IsRefused(int controlPx, string rowStep, int rowPx)
    {
        var result = Create(controlSize: CustomSize(controlPx), tableRowHeight: Size(rowStep));

        result.Errors.Should().ContainSingle();
        result.FirstError.Code.Should().Be("SystemSettings.ThemeRowHeightBelowControlHeight");
        result.FirstError.Metadata!["args"].Should().BeEquivalentTo(new object[] { rowPx, controlPx });
    }

    [Theory]
    [InlineData("comfortable", 39)]
    [InlineData("default", 35)]
    public void Create_ACustomRowBelowAControlStep_IsRefused(string controlStep, int rowPx)
    {
        var result = Create(controlSize: Size(controlStep), tableRowHeight: CustomSize(rowPx));

        result.FirstError.Code.Should().Be("SystemSettings.ThemeRowHeightBelowControlHeight");
    }

    [Fact]
    public void Create_ARowAsHighAsTheControls_IsAccepted()
    {
        Create(controlSize: CustomSize(48), tableRowHeight: CustomSize(48)).IsError.Should().BeFalse();
        Create(controlSize: CustomSize(40), tableRowHeight: Size("compact")).IsError.Should().BeFalse();
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
