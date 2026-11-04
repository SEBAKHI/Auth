using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth_API.Tests.Infrastructure;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// X04, RV1: the access-token lifetime and the clock skew are bounded where they are read. The
/// console refuses a value outside 1–1440 minutes and 0–300 seconds, but the settings file, the
/// environment and a stored override are not checked, and the revocation horizon is only a
/// bound if no token can be issued or accepted past it.
/// </summary>
/// <remarks>
/// Deliberate breaks, each seen red: drop either <c>Math.Clamp</c>; read the raw minutes again
/// in the secret-operation challenge.
/// </remarks>
public class JwtSettingsClampTests
{
    [Theory]
    [InlineData(100000, 1440)]
    [InlineData(1441, 1440)]
    [InlineData(1440, 1440)]
    [InlineData(120, 120)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void TheLifetime_IsReadWithinOneMinuteAndOneDay(int configured, int read)
    {
        new JwtSettings { AccessTokenLifetimeMinutes = configured }.AccessTokenLifetime
            .Should().Be(TimeSpan.FromMinutes(read));
    }

    [Theory]
    [InlineData(900, 300)]
    [InlineData(301, 300)]
    [InlineData(300, 300)]
    [InlineData(60, 60)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void TheSkew_IsReadWithinZeroAndFiveMinutes(int configured, int read)
    {
        new JwtSettings { ClockSkewSeconds = configured }.ClockSkew
            .Should().Be(TimeSpan.FromSeconds(read));
    }

    [Fact]
    public void TheRawValues_StayAsConfigured_ForBindingAndThePosture()
    {
        var settings = new JwtSettings { AccessTokenLifetimeMinutes = 100000, ClockSkewSeconds = 900 };

        settings.AccessTokenLifetimeMinutes.Should().Be(100000);
        settings.ClockSkewSeconds.Should().Be(900);
    }

    /// <summary>
    /// The raw values are read only where they must be: the settings class itself, the console's
    /// validation of a value being saved, and the posture that reports a clamped value. Every
    /// other reader goes through the bounded properties, the secret-operation challenge included.
    /// </summary>
    [Fact]
    public void NoProductionCode_ReadsTheRawLifetimeOrSkew_ExceptTheOwnersOfThem()
    {
        var allowed = new[]
        {
            "JwtSettings.cs",
            "SystemSettingsValueValidator.cs",
            "SystemSettingsRegistry.cs",
            "RevocationPostureContributor.cs",
        };
        var raw = new Regex(@"\.(AccessTokenLifetimeMinutes|ClockSkewSeconds)\b");

        var readers = ApiSourceScan.ProductionSources()
            .Where(source => raw.IsMatch(source.Source))
            .Select(source => Path.GetFileName(source.File))
            .ToList();

        readers.Should().NotBeEmpty("the scan must reach the production sources");
        readers.Should().BeSubsetOf(allowed);
    }
}
