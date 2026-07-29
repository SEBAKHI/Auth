using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Notifications;
using Auth.Infrastructure.Notifications.Outbox;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Outbox;

/// <summary>
/// Tests for the outbox pipeline: the enqueue-vs-synchronous switch in
/// NotificationService, dispatcher delivery/backoff/dead-letter/stale-reclaim,
/// and the enqueue signal driving immediate dispatch.
/// </summary>
public class NotificationOutboxTests
{
    private static NotificationOutboxMessage CreateMessage(
        int attemptCount = 0, string typeCode = "password-reset")
    {
        return new NotificationOutboxMessage(
            Guid.NewGuid(), typeCode, NotificationChannelType.Email,
            null, "user@example.com", "Jane", Guid.NewGuid(), "en",
            Guid.NewGuid(), Guid.NewGuid(), 2, "Subject", "<p>Body</p>", "Body",
            NotificationDeliveryStatus.Processing, attemptCount,
            DateTime.UtcNow, DateTime.UtcNow, null, null, DateTime.UtcNow, null);
    }

    private static RenderedNotification Rendered() => new()
    {
        Channel = NotificationChannelType.Email,
        RecipientAddress = "user@example.com",
        LanguageCode = "en",
        Subject = "Subject",
        BodyHtml = "<p>Body</p>",
        BodyText = "Body",
        TemplateId = Guid.NewGuid(),
        TemplateVersionId = Guid.NewGuid()
    };

    #region NotificationService routing

    private static NotificationService CreateService(
        bool useOutbox,
        Mock<INotificationRenderer> rendererMock,
        Mock<INotificationChannel> channelMock,
        Mock<INotificationOutboxRepository> outboxMock,
        Mock<INotificationDispatchSignal> signalMock)
    {
        var factoryMock = new Mock<INotificationChannelFactory>();
        factoryMock
            .Setup(f => f.GetChannel(NotificationChannelType.Email))
            .Returns(channelMock.Object);

        return new NotificationService(
            rendererMock.Object,
            factoryMock.Object,
            outboxMock.Object,
            signalMock.Object,
            TestHelpers.CreateOptions(new NotificationSettings { UseOutbox = useOutbox }),
            new Mock<ILogger<NotificationService>>().Object);
    }

    [Fact]
    public async Task SendAsync_OutboxDisabled_DeliversSynchronouslyThroughChannel()
    {
        var rendererMock = new Mock<INotificationRenderer>();
        rendererMock
            .Setup(r => r.RenderAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rendered());
        var channelMock = new Mock<INotificationChannel>();
        channelMock
            .Setup(c => c.SendAsync(It.IsAny<RenderedNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        var outboxMock = new Mock<INotificationOutboxRepository>();
        var signalMock = new Mock<INotificationDispatchSignal>();

        var service = CreateService(false, rendererMock, channelMock, outboxMock, signalMock);

        var result = await service.SendAsync(
            new NotificationRequest { TypeCode = "password-reset", RecipientAddress = "user@example.com" },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        channelMock.Verify(
            c => c.SendAsync(It.IsAny<RenderedNotification>(), It.IsAny<CancellationToken>()),
            Times.Once);
        outboxMock.Verify(
            o => o.EnqueueAsync(It.IsAny<NotificationOutboxMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_OutboxEnabled_EnqueuesRenderedContentAndSignals()
    {
        var rendererMock = new Mock<INotificationRenderer>();
        var rendered = Rendered();
        rendererMock
            .Setup(r => r.RenderAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rendered);
        var channelMock = new Mock<INotificationChannel>();
        var outboxMock = new Mock<INotificationOutboxRepository>();
        var signalMock = new Mock<INotificationDispatchSignal>();
        var triggeredBy = Guid.NewGuid();

        var service = CreateService(true, rendererMock, channelMock, outboxMock, signalMock);

        var result = await service.SendAsync(
            new NotificationRequest
            {
                TypeCode = "password-reset",
                RecipientAddress = "user@example.com",
                TriggeredBy = triggeredBy
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        outboxMock.Verify(o => o.EnqueueAsync(
            It.Is<NotificationOutboxMessage>(m =>
                m.NotificationTypeCode == "password-reset" &&
                m.Subject == rendered.Subject &&
                m.BodyHtml == rendered.BodyHtml &&
                m.TemplateVersionId == rendered.TemplateVersionId &&
                m.CreatedBy == triggeredBy &&
                m.Status == NotificationDeliveryStatus.Pending),
            It.IsAny<CancellationToken>()), Times.Once);
        signalMock.Verify(s => s.Notify(), Times.Once);
        channelMock.Verify(
            c => c.SendAsync(It.IsAny<RenderedNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Dispatcher

    private static (NotificationOutboxDispatcher Dispatcher,
        Mock<INotificationOutboxRepository> Repo,
        Mock<INotificationChannel> Channel,
        NotificationDispatchSignal Signal) CreateDispatcher(NotificationSettings settings)
    {
        var repoMock = new Mock<INotificationOutboxRepository>();
        var services = new ServiceCollection();
        services.AddScoped(_ => repoMock.Object);
        var provider = services.BuildServiceProvider();

        var channelMock = new Mock<INotificationChannel>();
        channelMock.SetupGet(c => c.Channel).Returns(NotificationChannelType.Email);
        var factoryMock = new Mock<INotificationChannelFactory>();
        factoryMock
            .Setup(f => f.GetChannel(NotificationChannelType.Email))
            .Returns(channelMock.Object);

        var signal = new NotificationDispatchSignal();
        var dispatcher = new NotificationOutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            signal,
            factoryMock.Object,
            TestHelpers.CreateOptions(settings),
            new Mock<ILogger<NotificationOutboxDispatcher>>().Object);

        return (dispatcher, repoMock, channelMock, signal);
    }

    private static async Task RunOneCycleAsync(
        NotificationOutboxDispatcher dispatcher,
        Func<Task> waitForOutcome)
    {
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            await waitForOutcome().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Dispatcher_SuccessfulSend_MarksMessageSent()
    {
        var settings = new NotificationSettings { UseOutbox = true, PollIntervalSeconds = 60 };
        var (dispatcher, repoMock, channelMock, _) = CreateDispatcher(settings);

        var message = CreateMessage();
        var sent = new TaskCompletionSource();
        var claims = 0;
        repoMock
            .Setup(r => r.ClaimBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claims) == 1 ? [message] : []);
        channelMock
            .Setup(c => c.SendAsync(It.IsAny<RenderedNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        repoMock
            .Setup(r => r.MarkSentAsync(message.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => sent.TrySetResult());

        await RunOneCycleAsync(dispatcher, () => sent.Task);

        // password-reset is a sensitive type: the body must be redacted at rest
        // the moment delivery succeeds.
        repoMock.Verify(r => r.MarkSentAsync(message.Id, true, It.IsAny<CancellationToken>()), Times.Once);
        channelMock.Verify(c => c.SendAsync(
            It.Is<RenderedNotification>(n => n.Subject == "Subject" && n.RecipientAddress == "user@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatcher_SuccessfulSend_NonSensitiveType_KeepsBody()
    {
        var settings = new NotificationSettings { UseOutbox = true, PollIntervalSeconds = 60 };
        var (dispatcher, repoMock, channelMock, _) = CreateDispatcher(settings);

        var message = CreateMessage(typeCode: "welcome-email");
        var sent = new TaskCompletionSource();
        var claims = 0;
        repoMock
            .Setup(r => r.ClaimBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claims) == 1 ? [message] : []);
        channelMock
            .Setup(c => c.SendAsync(It.IsAny<RenderedNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        repoMock
            .Setup(r => r.MarkSentAsync(message.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => sent.TrySetResult());

        await RunOneCycleAsync(dispatcher, () => sent.Task);

        repoMock.Verify(r => r.MarkSentAsync(message.Id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispatcher_FailedSend_SchedulesRetryWithBackoff()
    {
        var settings = new NotificationSettings { UseOutbox = true, PollIntervalSeconds = 60, MaxAttempts = 5 };
        var (dispatcher, repoMock, channelMock, _) = CreateDispatcher(settings);

        var message = CreateMessage(attemptCount: 1);
        var failed = new TaskCompletionSource();
        var claims = 0;
        repoMock
            .Setup(r => r.ClaimBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref claims) == 1 ? [message] : []);
        channelMock
            .Setup(c => c.SendAsync(It.IsAny<RenderedNotification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)Auth.Domain.Errors.NotificationErrors.SendFailed);
        repoMock
            .Setup(r => r.MarkFailedAsync(
                message.Id, It.IsAny<string>(), It.IsAny<DateTime>(), settings.MaxAttempts, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => failed.TrySetResult());

        await RunOneCycleAsync(dispatcher, () => failed.Task);

        // Attempt 1 → backoff 4^1 = 4 minutes.
        repoMock.Verify(r => r.MarkFailedAsync(
            message.Id,
            It.IsAny<string>(),
            It.Is<DateTime>(next =>
                next > DateTime.UtcNow.AddMinutes(3) && next < DateTime.UtcNow.AddMinutes(5)),
            settings.MaxAttempts,
            It.IsAny<CancellationToken>()), Times.Once);
        repoMock.Verify(
            r => r.MarkSentAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Dispatcher_Startup_ReclaimsStaleProcessingRows()
    {
        var settings = new NotificationSettings { UseOutbox = true, PollIntervalSeconds = 60, StaleClaimMinutes = 5 };
        var (dispatcher, repoMock, _, _) = CreateDispatcher(settings);

        var reclaimed = new TaskCompletionSource();
        repoMock
            .Setup(r => r.ReclaimStaleAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2)
            .Callback(() => reclaimed.TrySetResult());
        repoMock
            .Setup(r => r.ClaimBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await RunOneCycleAsync(dispatcher, () => reclaimed.Task);

        repoMock.Verify(r => r.ReclaimStaleAsync(
            It.Is<DateTime>(before =>
                before > DateTime.UtcNow.AddMinutes(-6) && before < DateTime.UtcNow.AddMinutes(-4)),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Dispatcher_OutboxDisabled_DoesNothing()
    {
        var settings = new NotificationSettings { UseOutbox = false };
        var (dispatcher, repoMock, _, signal) = CreateDispatcher(settings);

        await dispatcher.StartAsync(CancellationToken.None);
        signal.Notify();
        await Task.Delay(200);
        await dispatcher.StopAsync(CancellationToken.None);

        repoMock.Verify(r => r.ClaimBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ComputeNextAttempt_GrowsExponentially()
    {
        var now = DateTime.UtcNow;

        NotificationOutboxDispatcher.ComputeNextAttempt(0).Should().BeCloseTo(now.AddMinutes(1), TimeSpan.FromSeconds(5));
        NotificationOutboxDispatcher.ComputeNextAttempt(1).Should().BeCloseTo(now.AddMinutes(4), TimeSpan.FromSeconds(5));
        NotificationOutboxDispatcher.ComputeNextAttempt(2).Should().BeCloseTo(now.AddMinutes(16), TimeSpan.FromSeconds(5));
        NotificationOutboxDispatcher.ComputeNextAttempt(3).Should().BeCloseTo(now.AddMinutes(64), TimeSpan.FromSeconds(5));
        // Capped so the exponent never explodes.
        NotificationOutboxDispatcher.ComputeNextAttempt(10).Should().BeCloseTo(now.AddMinutes(256), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DispatchSignal_NotifyWakesWaiter_AndTimeoutFallsThrough()
    {
        var signal = new NotificationDispatchSignal();

        // Timeout path completes without a signal.
        var timedOut = signal.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        await timedOut.WaitAsync(TimeSpan.FromSeconds(2));

        // Signal path wakes promptly.
        var waiting = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        signal.Notify();
        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    #endregion
}
