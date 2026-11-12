using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Domain.Errors;
using Auth_API.Common.RateLimiting;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS5 (BD6, D-S13-1 (a), RL1 "bot-fallback", IP9): when an enforced check cannot run, a
/// sign-up start is refused and a sign-in, a code start or a reset request goes on through the
/// per-address allowance (5 per 300 s by default), refused once it is spent; Closed refuses all
/// four. The allowance is keyed by the settings version, as every per-address limit is.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the key without the version (a saved change no longer
/// opens fresh windows); the sign-up start let through the allowance.
/// </remarks>
public class BotDefenseFailModeTests
{
    private int _version = 7;

    private BotFallbackLimiter Allowance(int permits = 5) =>
        new(() => _version, () => new FixedWindowBudget(permits, 300));

    [Theory]
    [InlineData(BotDefenseActions.Login)]
    [InlineData(BotDefenseActions.ForgotPassword)]
    [InlineData(BotDefenseActions.EmailCode)]
    public void PerSurface_TheWaysBackIntoAnAccount_GoOnThroughTheAllowance_ThenAreRefused(string action)
    {
        var policy = new BotDefenseFailurePolicy(Allowance(permits: 5));

        for (var i = 0; i < 5; i++)
        {
            policy.Decide(BotDefenseFailMode.PerSurface, action, "203.0.113.9").IsError.Should().BeFalse();
        }

        var sixth = policy.Decide(BotDefenseFailMode.PerSurface, action, "203.0.113.9");
        sixth.IsError.Should().BeTrue();
        sixth.FirstError.Code.Should().Be(BotDefenseErrors.Unavailable.Code);
        policy.Decide(BotDefenseFailMode.PerSurface, action, "198.51.100.1").IsError.Should().BeFalse(
            "each address has its own allowance");
    }

    [Fact]
    public void PerSurface_ASignUpStart_IsRefused()
    {
        var allowance = new CountingAllowance(permits: 5);

        var decision = new BotDefenseFailurePolicy(allowance).Decide(
            BotDefenseFailMode.PerSurface, BotDefenseActions.Register, "203.0.113.9");

        decision.FirstError.Code.Should().Be(BotDefenseErrors.Unavailable.Code);
        allowance.Asked.Should().BeEmpty("nobody is locked out of an account that does not exist yet");
    }

    [Theory]
    [InlineData(BotDefenseActions.Login)]
    [InlineData(BotDefenseActions.Register)]
    [InlineData(BotDefenseActions.ForgotPassword)]
    [InlineData(BotDefenseActions.EmailCode)]
    public void Closed_RefusesAllFour(string action)
    {
        var allowance = new CountingAllowance(permits: 5);

        var decision = new BotDefenseFailurePolicy(allowance).Decide(BotDefenseFailMode.Closed, action, "203.0.113.9");

        decision.FirstError.Code.Should().Be(BotDefenseErrors.Unavailable.Code);
        allowance.Asked.Should().BeEmpty();
    }

    [Fact]
    public void TheAllowanceKey_CarriesTheSettingsVersion_AndASavedChangeOpensAFreshWindow()
    {
        var allowance = Allowance(permits: 1);

        allowance.KeyFor("203.0.113.9").Should().Be("v7:203.0.113.9");
        allowance.KeyFor(null).Should().Be("v7:unknown", "a request without an address shares one bucket, on purpose");

        allowance.TryAcquire("203.0.113.9").Should().BeTrue();
        allowance.TryAcquire("203.0.113.9").Should().BeFalse();

        _version = 8;
        allowance.TryAcquire("203.0.113.9").Should().BeTrue("a saved limit applies at once, through a fresh partition");
    }
}
