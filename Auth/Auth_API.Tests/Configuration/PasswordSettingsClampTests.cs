using Auth.Application.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// X12, LK1.6: the lock settings are bound from files with no validation, so the
/// normalizer clamps them on every rebind. Hard′ stays within 10–100 (NIST SP
/// 800-63B-4 §3.2.2 allows at most 100), and Max′ stays within 1–Hard′, so a timed
/// lock always comes before the hard one.
/// </summary>
/// <remarks>
/// Deliberate break: remove the clamp of <c>ConsecutiveFailureLimit</c>. A file
/// value of 500 then survives, and the 100-failure ceiling NIST asks for is gone.
/// </remarks>
public class PasswordSettingsClampTests
{
    [Theory]
    [InlineData(100, 5, 100, 5)]
    [InlineData(500, 5, 100, 5)]
    [InlineData(5, 5, 10, 5)]
    [InlineData(0, 5, 10, 5)]
    [InlineData(-3, 5, 10, 5)]
    [InlineData(100, 200, 100, 100)]
    [InlineData(20, 50, 20, 20)]
    [InlineData(100, 0, 100, 1)]
    [InlineData(100, -1, 100, 1)]
    public void Apply_ClampsTheLimitThenTheThreshold(int limit, int max, int expectedLimit, int expectedMax)
    {
        var settings = new PasswordSettings { ConsecutiveFailureLimit = limit, MaxFailedAttempts = max };

        PasswordSettingsNormalizer.Apply(settings);

        settings.ConsecutiveFailureLimit.Should().Be(expectedLimit);
        settings.MaxFailedAttempts.Should().Be(expectedMax);
    }

    [Fact]
    public void Defaults_AreAlreadyNormal()
    {
        var settings = new PasswordSettings();

        PasswordSettingsNormalizer.Apply(settings);

        settings.ConsecutiveFailureLimit.Should().Be(100);
        settings.MaxFailedAttempts.Should().Be(5);
        settings.ConsecutiveFailureMode.Should().Be(ConsecutiveFailureMode.Enforce);
        settings.EnforcedHardLimit.Should().Be(100);
    }

    [Fact]
    public void Host_RegistersTheNormalizerAsAPostConfigure()
    {
        // The clamp protects nothing unless it runs on the options consumers read.
        var program = File.ReadAllText(Path.Combine(
            Infrastructure.ApiSourceScan.SolutionDirectory(), "Auth_API", "Program.cs"));

        program.Should().Contain("builder.Services.PostConfigure<PasswordSettings>(PasswordSettingsNormalizer.Apply);");
    }
}
