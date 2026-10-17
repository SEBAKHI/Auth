using System.Threading.RateLimiting;
using Auth.Application.Configuration;
using Auth.Application.SystemSettings;
using Auth.Infrastructure.Persistence;
using Auth_API.Common.RateLimiting;
using Auth_API.Modules.Internal.Controllers;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// The pieces of the token budget per application (OI-178) below the hosts: the read-only jti
/// check through Dapper, the settings pull that carries the Gateway's new pair, and the limiter
/// that refuses an address past its allowance of assertions that did not verify.
/// </summary>
public class TokenBudgetPerApplicationPersistenceTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task IsSpentAsync_SeeksThePrimaryKey_AndOnlyReads(int found, bool expected)
    {
        var applicationId = Guid.NewGuid();
        var db = new RecordingDbConnectionFactory(affectedRows: 0, scalarFor: _ => found);

        var spent = await new ClientAssertionJtiStore(db).IsSpentAsync(applicationId, "jti-1", CancellationToken.None);

        spent.Should().Be(expected);
        var command = db.Commands.Should().ContainSingle().Subject;
        command.CommandText.Should().Be(ClientAssertionJtiStore.IsRecorded);
        command.CommandText.Should().MatchRegex(
            @"SELECT CASE WHEN EXISTS \(\s*SELECT 1 FROM \[dbo\]\.\[ClientAssertionJtis\]\s*WHERE \[ApplicationId\] = @ApplicationId AND \[Jti\] = @Jti",
            "both columns of the primary key, so it is one seek");
        command.CommandText.Should().NotContainAny(["INSERT", "UPDATE", "DELETE"], "classifying spends nothing");
        command.Parameters["ApplicationId"].Should().Be(applicationId);
    }

    [Fact]
    public void TheSettingsPull_CarriesTheAssertionPair_FromConfiguration_WithTheRegistryDefaults()
    {
        GatewayRateLimitsResponse Pull(Dictionary<string, string?> values)
        {
            var controller = new GatewayRuntimeSettingsController(
                new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
                TestHelpers.CreateOptions(new GatewaySettings { ExpectedToken = "gateway-token" }),
                Mock.Of<ISystemSettingsReloader>(reloader => reloader.Version == 7))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controller.HttpContext.Request.Headers["X-Gateway-Token"] = "gateway-token";

            return ((GatewayRuntimeSettingsResponse)((OkObjectResult)controller.Get()).Value!).RateLimits;
        }

        var defaults = Pull([]);
        defaults.TokenAssertionPermitLimit.Should().Be(6000);
        defaults.TokenAssertionWindowSeconds.Should().Be(60);

        var saved = Pull(new()
        {
            ["GatewayRateLimiting:TokenAssertionPermitLimit"] = "9000",
            ["GatewayRateLimiting:TokenAssertionWindowSeconds"] = "30",
        });
        saved.TokenAssertionPermitLimit.Should().Be(9000, "a value saved in the console reaches the Gateway on its next pull");
        saved.TokenAssertionWindowSeconds.Should().Be(30);
    }

    [Fact]
    public async Task TheRefusedClass_RefusesEveryRequest_AndCarriesNoRetryHint()
    {
        var partition = TokenRequestClass.Refused.Partition(
            settingsVersion: 0, "198.51.100.66", () => new FixedWindowBudget(1, 60), () => new FixedWindowBudget(1, 60));
        using var limiter = partition.Factory(partition.PartitionKey);

        limiter.IdleDuration.Should().BeNull("it holds nothing, so it never needs replacing");
        limiter.GetStatistics()!.CurrentAvailablePermits.Should().Be(0);

        using var attempted = limiter.AttemptAcquire();
        using var awaited = await limiter.AcquireAsync();

        attempted.IsAcquired.Should().BeFalse();
        awaited.IsAcquired.Should().BeFalse("the rate limiter middleware tries again with AcquireAsync after a refusal");
        attempted.MetadataNames.Should().BeEmpty();
        attempted.TryGetMetadata(MetadataName.RetryAfter.Name, out var retryAfter).Should().BeFalse(
            "Program.cs then answers with its own short Retry-After");
        retryAfter.Should().BeNull();
    }
}
