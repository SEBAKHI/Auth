using Auth.Application.Configuration;
using Auth.Application.Security;
using Auth.Infrastructure.Security;
using Microsoft.Extensions.Logging;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security;

/// <summary>
/// A value outside its bounds is applied clamped, and the console shows the value
/// as configured. The posture line is how an operator learns it was not taken as
/// written: one Warning per value, naming the configured and the applied value.
/// </summary>
public class RetentionBoundsPostureContributorTests
{
    private static RetentionBoundsPostureContributor Create(
        AccountDeletionSettings accountDeletion, DataRetentionSettings? dataRetention = null) =>
        new(TestHelpers.CreateOptions(accountDeletion), TestHelpers.CreateOptions(dataRetention ?? new DataRetentionSettings()));

    private static IReadOnlyList<CapturedLogEntry> Render(SecurityPosture posture)
    {
        var logger = new CapturingLogger<RetentionBoundsPostureContributorTests>();
        foreach (var line in posture.Lines)
        {
#pragma warning disable CA2254
            logger.Log(line.Level, line.Exception, line.MessageTemplate, line.Arguments.ToArray());
#pragma warning restore CA2254
        }

        return logger.Entries;
    }

    [Fact]
    public async Task AnAuditRetentionOfZero_IsNamedWithTheValueApplied()
    {
        var posture = await Create(new AccountDeletionSettings { AuditLogRetentionDays = 0 })
            .EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Weak);
        var line = Render(posture).Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Message.Should().Be(
            "AccountDeletion:AuditLogRetentionDays = 0 is outside 1095–3650: the value applied is 1095. Set a value within the bounds");
    }

    [Fact]
    public async Task TheExpiredDataPoll_IsCoveredToo()
    {
        var posture = await Create(new AccountDeletionSettings(), new DataRetentionSettings { WorkerPollMinutes = 100_000 })
            .EvaluateAsync(CancellationToken.None);

        Render(posture).Should().ContainSingle()
            .Which.Message.Should().StartWith("DataRetention:WorkerPollMinutes = 100000 is outside 1–1440");
    }

    [Fact]
    public async Task ShippedDefaults_WriteNothing()
    {
        var posture = await Create(new AccountDeletionSettings()).EvaluateAsync(CancellationToken.None);

        posture.Level.Should().Be(SecurityPostureLevel.Ok);
        posture.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task AReservationShorterThanTheAuditRetention_IsNotAWarning()
    {
        // Raised to the audit retention by design, inside its own bounds.
        var posture = await Create(new AccountDeletionSettings
        {
            IdentifierReservationDays = 1095,
            AuditLogRetentionDays = 3650
        }).EvaluateAsync(CancellationToken.None);

        posture.Lines.Should().BeEmpty();
    }
}
