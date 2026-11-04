using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Infrastructure.Authentication;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Security;

/// <summary>
/// X04, RV1 and RV2 (e): the posture reports a revocation list that did not load at startup
/// (Critical, <c>revocation.rehydration-failed</c>) and a lifetime or skew configured outside the
/// bounds it is read within (Warning, <c>revocation.lifetime-clamped</c>). The state is built
/// from the reasons alone, so a reload that changes nothing here writes nothing.
/// </summary>
/// <remarks>Deliberate break: drop either check; its test turns red.</remarks>
public class RevocationPostureContributorTests
{
    private static RevocationRehydrationStatus Loaded()
    {
        var status = new RevocationRehydrationStatus();
        status.RecordLoaded();
        return status;
    }

    private static Task<SecurityPosture> Evaluate(RevocationRehydrationStatus status, JwtSettings jwt) =>
        new RevocationPostureContributor(status, TestHelpers.CreateOptions(jwt)).EvaluateAsync(CancellationToken.None);

    [Fact]
    public void ItsName_IsRevocation()
    {
        new RevocationPostureContributor(Loaded(), TestHelpers.CreateOptions(new JwtSettings())).Name.Should().Be("revocation");
    }

    [Fact]
    public async Task ALoadedList_WithinTheBounds_IsOk()
    {
        var posture = await Evaluate(Loaded(), new JwtSettings());

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Reasons.Should().BeEmpty();
        posture.State.Should().Be("ok");
        posture.Lines.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task AListNotLoaded_IsWeak_WithACriticalLine()
    {
        var posture = await Evaluate(new RevocationRehydrationStatus(), new JwtSettings());

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Reasons.Should().Equal("revocation.rehydration-failed");
        posture.Lines.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Critical);
        posture.State.Should().Be("revocation.rehydration-failed");
    }

    [Theory]
    [InlineData(100000, 60)]
    [InlineData(0, 60)]
    [InlineData(15, 900)]
    [InlineData(15, -5)]
    public async Task ALifetimeOrSkewOutsideItsBounds_IsWeak_WithAWarningLine(int lifetime, int skew)
    {
        var posture = await Evaluate(Loaded(), new JwtSettings { AccessTokenLifetimeMinutes = lifetime, ClockSkewSeconds = skew });

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        posture.Reasons.Should().Equal("revocation.lifetime-clamped");
        posture.Lines.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task TheState_DependsOnTheReasonsOnly()
    {
        var first = await Evaluate(Loaded(), new JwtSettings { AccessTokenLifetimeMinutes = 2000 });
        var second = await Evaluate(Loaded(), new JwtSettings { AccessTokenLifetimeMinutes = 3000 });

        second.State.Should().Be(first.State);
    }
}
