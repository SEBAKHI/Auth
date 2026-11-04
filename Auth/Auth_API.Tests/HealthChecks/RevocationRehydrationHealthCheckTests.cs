using Auth.Infrastructure.Authentication;
using Auth_API.Common.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth_API.Tests.HealthChecks;

/// <summary>
/// X04, RV2 (e): <c>/ready</c> reports the token revocation list. Until a load succeeds a token
/// revoked before the start is accepted again, so the process is not ready; the first success
/// turns it ready. The description never carries a count or an exception: <c>/ready</c> is
/// anonymous.
/// </summary>
/// <remarks>Deliberate break: answer Healthy whatever the status; the first test turns red.</remarks>
public class RevocationRehydrationHealthCheckTests
{
    private static async Task<HealthCheckResult> Check(RevocationRehydrationStatus status) =>
        await new RevocationRehydrationHealthCheck(status).CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "revocation", new RevocationRehydrationHealthCheck(status), HealthStatus.Unhealthy, ["ready"]),
        });

    [Fact]
    public async Task NotLoaded_IsUnhealthy_WithAConstantDescription()
    {
        var status = new RevocationRehydrationStatus();
        status.RecordFailed();

        var result = await Check(status);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Token revocation list not loaded.");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task Loaded_IsHealthy_AndStaysSoWhenALaterReadFails()
    {
        var status = new RevocationRehydrationStatus();
        status.RecordFailed();
        status.RecordLoaded();
        status.RecordFailed();

        var result = await Check(status);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Token revocation list loaded.");
        status.FailedAttempts.Should().Be(1, "only the failures before the first load are counted");
    }
}
