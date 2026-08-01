using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Notifications.Outbox;

/// <summary>
/// Background dispatcher for the notification outbox. Wakes on the in-process
/// enqueue signal (mail triggered by a request dispatches within the same
/// process lifetime — no dependency on long-lived polling under IIS app-pool
/// idling) or on the poll-interval fallback. At startup it reclaims Processing
/// rows orphaned by a crashed/recycled worker. Delivery is at-least-once.
/// </summary>
public class NotificationOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationDispatchSignal _signal;
    private readonly INotificationChannelFactory _channelFactory;
    private readonly IOptionsMonitor<NotificationSettings> _settings;
    private readonly ILogger<NotificationOutboxDispatcher> _logger;

    public NotificationOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        INotificationDispatchSignal signal,
        INotificationChannelFactory channelFactory,
        IOptionsMonitor<NotificationSettings> settings,
        ILogger<NotificationOutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _channelFactory = channelFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Settings are read live each cycle so poll/batch/attempt changes —
        // and the UseOutbox toggle itself — apply without a restart.
        _logger.LogInformation("Notification outbox dispatcher started.");

        var catchUpDone = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = _settings.CurrentValue;
                if (!settings.UseOutbox)
                {
                    // Outbox off: idle-poll so enabling it later is picked up.
                    catchUpDone = false;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, settings.PollIntervalSeconds)), stoppingToken);
                    continue;
                }

                if (!catchUpDone)
                {
                    // Cold-start / re-enable catch-up: reclaim orphans and
                    // drain any backlog immediately.
                    await SafeReclaimAsync(stoppingToken);
                    _signal.Notify();
                    catchUpDone = true;
                }

                await _signal.WaitAsync(settings.PollInterval, stoppingToken);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await SafeReclaimAsync(stoppingToken);

                // Drain until the queue is empty so a burst finishes in one wake-up.
                while (await DispatchBatchAsync(stoppingToken) > 0)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The dispatcher must never die from a transient failure (e.g.
                // the database briefly unavailable); the next cycle retries.
                _logger.LogError(ex, "Notification outbox dispatch cycle failed.");
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();

        var batch = await repository.ClaimBatchAsync(_settings.CurrentValue.BatchSize, cancellationToken);
        foreach (var message in batch)
        {
            var channel = _channelFactory.GetChannel(message.Channel);
            if (channel is null)
            {
                await repository.MarkFailedAsync(
                    message.Id,
                    $"No delivery channel registered for '{message.Channel}'.",
                    ComputeNextAttempt(message.AttemptCount),
                    _settings.CurrentValue.MaxAttempts,
                    cancellationToken);
                continue;
            }

            var result = await channel.SendAsync(
                new RenderedNotification
                {
                    Channel = message.Channel,
                    RecipientAddress = message.Recipient,
                    RecipientName = message.RecipientName,
                    LanguageCode = message.LanguageCode,
                    Subject = message.Subject,
                    BodyHtml = message.BodyHtml,
                    BodyText = message.BodyText ?? string.Empty,
                    TemplateId = message.TemplateId,
                    TemplateVersionId = message.TemplateVersionId,
                    TemplateVersionNumber = message.TemplateVersionNumber
                },
                cancellationToken);

            if (result.IsError)
            {
                var nextAttempt = ComputeNextAttempt(message.AttemptCount);
                await repository.MarkFailedAsync(
                    message.Id,
                    result.FirstError.Description,
                    nextAttempt,
                    _settings.CurrentValue.MaxAttempts,
                    cancellationToken);

                if (message.AttemptCount + 1 >= _settings.CurrentValue.MaxAttempts)
                {
                    _logger.LogError(
                        "Outbox message {MessageId} ({TypeCode}) dead-lettered after {Attempts} attempts: {Error}",
                        message.Id, message.NotificationTypeCode, message.AttemptCount + 1,
                        result.FirstError.Description);
                }
                else
                {
                    _logger.LogWarning(
                        "Outbox message {MessageId} ({TypeCode}) failed attempt {Attempt}; retry at {NextAttempt}: {Error}",
                        message.Id, message.NotificationTypeCode, message.AttemptCount + 1,
                        nextAttempt, result.FirstError.Description);
                }
            }
            else
            {
                await repository.MarkSentAsync(
                    message.Id,
                    redactBody: NotificationTypeCodes.SensitiveContentCodes.Contains(
                        message.NotificationTypeCode),
                    cancellationToken);
            }
        }

        return batch.Count;
    }

    /// <summary>
    /// Exponential backoff: 1, 4, 16, 64... minutes for attempts 0, 1, 2, 3...
    /// </summary>
    public static DateTime ComputeNextAttempt(int attemptCount) =>
        DateTime.UtcNow.AddMinutes(Math.Pow(4, Math.Min(attemptCount, 4)));

    private async Task SafeReclaimAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();

            var reclaimed = await repository.ReclaimStaleAsync(
                DateTime.UtcNow - _settings.CurrentValue.StaleClaimAge, cancellationToken);
            if (reclaimed > 0)
            {
                _logger.LogWarning(
                    "Reclaimed {Count} orphaned Processing outbox message(s) from a previous worker.",
                    reclaimed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to reclaim stale outbox messages.");
        }
    }
}
