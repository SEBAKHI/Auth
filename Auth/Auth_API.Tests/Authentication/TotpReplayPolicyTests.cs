using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="TotpReplayPolicy"/>: the rollout switch deciding
/// whether a reused authenticator-app code is refused.
/// </summary>
public class TotpReplayPolicyTests
{
    [Fact]
    public void RejectReused_IsOnByDefault()
    {
        var policy = new TotpReplayPolicy(Monitor(new TwoFactorSettings()).Object);

        policy.RejectReusedCodes.Should().BeTrue();
    }

    [Fact]
    public void RejectReused_ReadsCurrentValuePerCall()
    {
        // The switch is an incident lever: a System Settings save rebinds the
        // monitor, and the very next code check must see it. A value captured when
        // the policy was built would keep refusing — or keep accepting — until a
        // restart.
        var current = new TwoFactorSettings { RejectReusedCodes = true };
        var monitor = Monitor(current);
        var policy = new TotpReplayPolicy(monitor.Object);

        policy.RejectReusedCodes.Should().BeTrue();

        monitor.Setup(m => m.CurrentValue).Returns(new TwoFactorSettings { RejectReusedCodes = false });
        policy.RejectReusedCodes.Should().BeFalse("the policy follows the monitor without being rebuilt");

        monitor.Setup(m => m.CurrentValue).Returns(new TwoFactorSettings { RejectReusedCodes = true });
        policy.RejectReusedCodes.Should().BeTrue("and back again");
    }

    private static Mock<IOptionsMonitor<TwoFactorSettings>> Monitor(TwoFactorSettings value)
    {
        var monitor = new Mock<IOptionsMonitor<TwoFactorSettings>>();
        monitor.Setup(m => m.CurrentValue).Returns(value);
        return monitor;
    }
}
