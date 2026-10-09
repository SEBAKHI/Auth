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

    [Fact]
    public void ACustomisedAppearance_RoundTrips()
    {
        var theme = PlatformTheme.Create(
            new ThemeColorChoice("custom", "#112233", "#445566"),
            new ThemeColorChoice("violet"),
            new ThemeColorChoice("custom", "#aabbcc", "#ddeeff"),
            "large",
            "bold").Value;

        var json = PlatformSettingsRepository.ThemeJson.Write(theme);

        json.Should().Be(
            "{\"base\":{\"preset\":\"custom\",\"light\":\"#112233\",\"dark\":\"#445566\"}," +
            "\"theme\":{\"preset\":\"violet\"}," +
            "\"chart\":{\"preset\":\"custom\",\"light\":\"#aabbcc\",\"dark\":\"#ddeeff\"}," +
            "\"radius\":\"large\",\"menuAccent\":\"bold\"}");
        PlatformSettingsRepository.ThemeJson.Read(json).Should().Be(theme);
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
