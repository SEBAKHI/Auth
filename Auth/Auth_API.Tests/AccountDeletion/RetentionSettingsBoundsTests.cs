using Auth.Application.Configuration;
using Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// The account-deletion and retention values are held inside the console's own
/// bounds where they are read, because a settings file, an environment variable
/// and a stored override reach the binder without passing through the console.
/// </summary>
public class RetentionSettingsBoundsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100_000, 1440)]
    public void WorkerPollInterval_IsHeldBetweenAMinuteAndADay(int configured, int appliedMinutes)
    {
        // Zero spun the loop without a pause; below zero or past what the delay
        // accepts, the delay threw out of the worker loop and stopped the host.
        new AccountDeletionSettings { WorkerPollMinutes = configured }.WorkerPollInterval
            .Should().Be(TimeSpan.FromMinutes(appliedMinutes));
    }

    [Fact]
    public void GracePeriod_OfZero_IsOneDay()
    {
        // Zero made every deletion request due at once: executed within one
        // poll, with no window for the account's owner to cancel it.
        new AccountDeletionSettings { GraceDays = 0 }.GracePeriod.Should().Be(TimeSpan.FromDays(1));
    }

    [Fact]
    public void MaxExecutionAttempts_OfOne_IsTwo()
    {
        // At one, the first transient failure dead-letters the request for good.
        new AccountDeletionSettings { MaxExecutionAttempts = 1 }.EffectiveMaxExecutionAttempts.Should().Be(2);
    }

    [Fact]
    public void OtherAccountDeletionValues_AreHeldInsideTheirBounds()
    {
        var settings = new AccountDeletionSettings
        {
            OtpExpirationMinutes = 0,
            WorkerBatchSize = 100_000,
            GraceDays = 10_000
        };

        settings.EffectiveOtpExpirationMinutes.Should().Be(1);
        settings.EffectiveWorkerBatchSize.Should().Be(500);
        settings.EffectiveGraceDays.Should().Be(365);
    }

    [Fact]
    public async Task DataRetentionPoll_PastWhatTheDelayAccepts_IsOneDay()
    {
        // The expired-data cleanup had a floor and no ceiling, so this value
        // threw out of its loop and stopped the API host.
        var interval = new DataRetentionSettings { WorkerPollMinutes = int.MaxValue }.WorkerPollInterval;

        interval.Should().Be(TimeSpan.FromDays(1));
        var delay = () => Task.Delay(interval, new CancellationToken(canceled: true));
        await delay.Should().ThrowAsync<TaskCanceledException>("a valid interval reaches the cancellation check");
    }

    [Fact]
    public void PublishedPolicy_QuotesTheValuesApplied_NotTheRawSettings()
    {
        var settings = new AccountDeletionSettings
        {
            LoginAttemptRetentionDays = 0,
            OutboxRetentionDays = int.MaxValue,
            GraceDays = 0,
            OtpExpirationMinutes = 0
        };

        var disclosure = GetPublishedPrivacyPolicyQueryHandler.BuildDisclosure(settings, new DataControllerSettings());

        disclosure.LoginAttemptRetentionDays.Should().Be(30);
        disclosure.OutboxRetentionDays.Should().Be(3650);
        disclosure.GraceDays.Should().Be(1);
        disclosure.OtpValidityMinutes.Should().Be(1);
    }
}
