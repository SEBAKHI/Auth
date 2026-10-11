using Auth.Application.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// X12, LK1.6: the lock settings are bound from files with no validation, so the
/// normalizer clamps them on every rebind. Hard′ stays within 10–100 (NIST SP
/// 800-63B-4 §3.2.2 allows at most 100), and Max′ stays within 1–(Hard′ − 1), so a
/// timed lock always comes before the hard one: the failure statement tests the hard
/// limit first, so at Max′ = Hard′ the first lock would be the hard one (review 64, F7).
/// </summary>
/// <remarks>
/// Deliberate break: remove the clamp of <c>ConsecutiveFailureLimit</c>. A file
/// value of 500 then survives, and the 100-failure ceiling NIST asks for is gone.
/// Clamp Max′ to Hard′ again: the (10, 10) and (10, 20) rows turn red. Drop the
/// mode mapping: the undefined-mode test turns red.
/// </remarks>
public class PasswordSettingsClampTests
{
    [Theory]
    [InlineData(100, 5, 100, 5)]
    [InlineData(500, 5, 100, 5)]
    [InlineData(5, 5, 10, 5)]
    [InlineData(0, 5, 10, 5)]
    [InlineData(-3, 5, 10, 5)]
    [InlineData(100, 200, 100, 99)]
    [InlineData(20, 50, 20, 19)]
    [InlineData(10, 10, 10, 9)]
    [InlineData(10, 20, 10, 9)]
    [InlineData(100, 99, 100, 99)]
    [InlineData(100, 0, 100, 1)]
    [InlineData(100, -1, 100, 1)]
    public void Apply_ClampsTheLimitThenTheThreshold(int limit, int max, int expectedLimit, int expectedMax)
    {
        var settings = new PasswordSettings { ConsecutiveFailureLimit = limit, MaxFailedAttempts = max };

        PasswordSettingsNormalizer.Apply(settings);

        settings.ConsecutiveFailureLimit.Should().Be(expectedLimit);
        settings.MaxFailedAttempts.Should().Be(expectedMax);
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, 1440)]
    public void Apply_ClampsTheLockoutDuration(int minutes, int expected)
    {
        // At 0 a timed lock would lapse at once, and the per-address ceiling would
        // count an empty window (security review, G9).
        var settings = new PasswordSettings { LockoutDurationMinutes = minutes };

        PasswordSettingsNormalizer.Apply(settings);

        settings.LockoutDurationMinutes.Should().Be(expected);
    }

    [Fact]
    public void Apply_AnUndefinedMode_ReadsAsEnforce()
    {
        // A file or an environment variable may bind Password__ConsecutiveFailureMode=2.
        // Every consumer tests one named value, so it would run half Legacy (no hard
        // lock, no pending-deletion ceiling, the count kept), silently.
        var settings = new PasswordSettings { ConsecutiveFailureMode = (ConsecutiveFailureMode)2 };

        PasswordSettingsNormalizer.Apply(settings);

        settings.ConsecutiveFailureMode.Should().Be(ConsecutiveFailureMode.Enforce);
        settings.EnforcedHardLimit.Should().Be(settings.ConsecutiveFailureLimit);
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
