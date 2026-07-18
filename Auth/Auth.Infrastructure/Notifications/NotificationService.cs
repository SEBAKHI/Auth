using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Notifications;

/// <summary>
/// Default notification service: renders through the database-managed template
/// pipeline, then either enqueues into the outbox (UseOutbox — delivery happens
/// out-of-request with retries, and the row doubles as the delivery log) or
/// dispatches synchronously through the channel strategy. Rendering always
/// happens at the call site so a template problem fails fast where the flow's
/// error semantics live; errors are returned, never thrown.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationRenderer _renderer;
    private readonly INotificationChannelFactory _channelFactory;
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly INotificationDispatchSignal _dispatchSignal;
    private readonly NotificationSettings _settings;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRenderer renderer,
        INotificationChannelFactory channelFactory,
        INotificationOutboxRepository outboxRepository,
        INotificationDispatchSignal dispatchSignal,
        IOptions<NotificationSettings> settings,
        ILogger<NotificationService> logger)
    {
        _renderer = renderer;
        _channelFactory = channelFactory;
        _outboxRepository = outboxRepository;
        _dispatchSignal = dispatchSignal;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> SendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken)
    {
        var rendered = await _renderer.RenderAsync(request, cancellationToken);
        if (rendered.IsError)
        {
            _logger.LogError(
                "Failed to render notification {TypeCode} for {Recipient}: {Error}",
                request.TypeCode, request.RecipientAddress, rendered.FirstError.Description);
            return rendered.Errors;
        }

        if (_settings.UseOutbox)
        {
            return await EnqueueAsync(request, rendered.Value, cancellationToken);
        }

        var channel = _channelFactory.GetChannel(request.Channel);
        if (channel is null)
        {
            _logger.LogError(
                "No delivery channel registered for {Channel} (notification {TypeCode})",
                request.Channel, request.TypeCode);
            return NotificationErrors.ChannelNotSupported(request.Channel.ToString());
        }

        return await channel.SendAsync(rendered.Value, cancellationToken);
    }

    private async Task<ErrorOr<Success>> EnqueueAsync(
        NotificationRequest request,
        RenderedNotification rendered,
        CancellationToken cancellationToken)
    {
        var message = NotificationOutboxMessage.Create(
            request.TypeCode,
            request.Channel,
            request.ApplicationId,
            rendered.RecipientAddress,
            rendered.RecipientName,
            request.RecipientUserId,
            rendered.LanguageCode,
            rendered.TemplateId,
            rendered.TemplateVersionId,
            rendered.TemplateVersionNumber,
            rendered.Subject,
            rendered.BodyHtml,
            rendered.BodyText,
            createdBy: request.TriggeredBy ?? request.RecipientUserId);

        await _outboxRepository.EnqueueAsync(message, cancellationToken);
        _dispatchSignal.Notify();

        _logger.LogInformation(
            "Notification {TypeCode} enqueued for {Recipient} [{Language}] as outbox message {MessageId}",
            request.TypeCode, rendered.RecipientAddress, rendered.LanguageCode, message.Id);

        return Result.Success;
    }
}
