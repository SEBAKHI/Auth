using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Persistence;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The stored form of the platform appearance, and what reading does with a
/// row that does not hold a valid one.
/// </summary>
public class PlatformThemeJsonTests
{
    [Fact]
    public void TheShippedPreset_IsStoredAsNull()
    {
        // NULL keeps "never customised" distinguishable in the table, and an
        // install that never opens the setting writes nothing.
        PlatformSettingsRepository.ThemeJson.Write(PlatformTheme.Default).Should().BeNull();
        PlatformSettingsRepository.ThemeJson.Read(null).Should().Be(PlatformTheme.Default);
    }

    /// <summary>
    /// A row as the API wrote it before the size choices existed: the exact
    /// string the earlier <c>Write</c> produced for this appearance (the
    /// round-trip test of that version asserted it character for character).
    /// </summary>
    private const string RowWrittenBeforeSizes =
        "{\"base\":{\"preset\":\"custom\",\"light\":\"#112233\",\"dark\":\"#445566\"}," +
        "\"theme\":{\"preset\":\"violet\"}," +
        "\"chart\":{\"preset\":\"custom\",\"light\":\"#aabbcc\",\"dark\":\"#ddeeff\"}," +
        "\"radius\":\"large\",\"menuAccent\":\"bold\"}";

    private static PlatformTheme Customised(
        ThemeSizeChoice controlSize, ThemeSizeChoice tableRowHeight, string headerIconSize = "google") =>
        PlatformTheme.Create(
            new ThemeColorChoice("custom", "#112233", "#445566"),
            new ThemeColorChoice("violet"),
            new ThemeColorChoice("custom", "#aabbcc", "#ddeeff"),
            "large",
            "bold",
            controlSize,
            tableRowHeight,
            headerIconSize).Value;

    [Fact]
    public void ACustomisedAppearance_RoundTrips()
    {
        var theme = Customised(new ThemeSizeChoice("custom", 30), new ThemeSizeChoice("comfortable"));

        var json = PlatformSettingsRepository.ThemeJson.Write(theme);

        json.Should().Be(
            RowWrittenBeforeSizes[..^1] +
            ",\"controlSize\":{\"preset\":\"custom\",\"px\":30}," +
            "\"tableRowHeight\":{\"preset\":\"comfortable\"}}");
        PlatformSettingsRepository.ThemeJson.Read(json).Should().Be(theme);
    }

    [Fact]
    public void AHeaderIconSizeOtherThanTheDefault_IsStoredAndReadBack()
    {
        var theme = Customised(ThemeSizeChoice.Default, ThemeSizeChoice.Default, "large");

        var json = PlatformSettingsRepository.ThemeJson.Write(theme);

        json.Should().EndWith(",\"headerIconSize\":\"large\"}");
        PlatformSettingsRepository.ThemeJson.Read(json).Should().Be(theme);
    }

    [Fact]
    public void TheDefaultHeaderIconSize_IsNotWritten()
    {
        // An application's appearance always holds the default, so its row
        // carries no header field; absent reads back as the default.
        var theme = Customised(ThemeSizeChoice.Default, ThemeSizeChoice.Default);

        PlatformSettingsRepository.ThemeJson.Write(theme).Should().NotContain("headerIconSize");
    }

    [Fact]
    public void ARowSavedBeforeTheHeaderIconSizeExisted_ReadsGoogle()
    {
        var theme = PlatformSettingsRepository.ThemeJson.Read(RowWrittenBeforeSizes);

        theme.HeaderIconSize.Should().Be("google");
    }

    [Theory]
    [InlineData(",\"headerIconSize\":\"huge\"}")]
    [InlineData(",\"headerIconSize\":7}")]
    [InlineData(",\"headerIconSize\":null}")]
    [InlineData(",\"headerIconSize\":{\"preset\":\"large\"}}")]
    public void AnUnreadableHeaderIconSize_ReadsGoogleAndKeepsEverythingElse(string field)
    {
        var theme = PlatformSettingsRepository.ThemeJson.Read(RowWrittenBeforeSizes[..^1] + field);

        theme.Should().Be(Customised(ThemeSizeChoice.Default, ThemeSizeChoice.Default));
    }

    [Fact]
    public void ARowSavedBeforeTheSizesExisted_KeepsItsColoursAndReadsTheDefaultSizes()
    {
        // Every sign-in page renders this value: a row from before the
        // upgrade must neither fail nor lose the administrator's colours.
        var theme = PlatformSettingsRepository.ThemeJson.Read(RowWrittenBeforeSizes);

        theme.Should().Be(Customised(ThemeSizeChoice.Default, ThemeSizeChoice.Default));
        theme.Should().NotBe(PlatformTheme.Default);
        theme.ControlHeightPx.Should().Be(36);
        theme.TableRowHeightPx.Should().Be(44);
    }

    [Theory]
    [InlineData(",\"controlSize\":{\"preset\":\"huge\"},\"tableRowHeight\":{\"preset\":\"default\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":\"custom\",\"px\":500},\"tableRowHeight\":{\"preset\":\"default\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":\"custom\",\"px\":56},\"tableRowHeight\":{\"preset\":\"compact\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":\"custom\"},\"tableRowHeight\":null}")]
    // The wrong JSON type: before the sizes were read field by field, each of
    // these failed the whole row and cost the colours too.
    [InlineData(",\"controlSize\":\"compact\",\"tableRowHeight\":{\"preset\":\"default\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":\"custom\",\"px\":30.5},\"tableRowHeight\":{\"preset\":\"default\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":\"custom\",\"px\":\"30\"},\"tableRowHeight\":{\"preset\":\"default\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":\"custom\",\"px\":99999999999},\"tableRowHeight\":{\"preset\":\"default\"}}")]
    [InlineData(",\"controlSize\":{\"preset\":7},\"tableRowHeight\":[]}")]
    public void ARowWhoseSizesAloneAreInvalid_KeepsItsColours(string sizes)
    {
        // A height cannot leave a palette half built, so a bad height costs
        // only itself: both sizes read as the default, the colours stay.
        var theme = PlatformSettingsRepository.ThemeJson.Read(RowWrittenBeforeSizes[..^1] + sizes);

        theme.Should().Be(Customised(ThemeSizeChoice.Default, ThemeSizeChoice.Default));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"base\":{\"preset\":\"unheard-of\"},\"theme\":{\"preset\":\"blue\"},\"chart\":{\"preset\":\"cyan\"},\"radius\":\"default\",\"menuAccent\":\"subtle\"}")]
    [InlineData("{\"base\":{\"preset\":\"neutral\"},\"theme\":{\"preset\":\"custom\",\"light\":\"red}\",\"dark\":\"#000000\"},\"chart\":{\"preset\":\"cyan\"},\"radius\":\"default\",\"menuAccent\":\"subtle\"}")]
    public void AnInvalidRow_ReadsAsTheShippedPreset(string json)
    {
        // A row edited by hand, or naming a colour a later registry dropped,
        // renders as the shipped preset instead of half a palette.
        PlatformSettingsRepository.ThemeJson.Read(json).Should().Be(PlatformTheme.Default);
    }
}
