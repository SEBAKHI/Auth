using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationOutboxMessageById;

/// <summary>
/// Handler returning one delivery-log entry with its rendered bodies (the
/// application name is resolved for the header).
/// </summary>
public class GetNotificationOutboxMessageByIdQueryHandler
    : IRequestHandler<GetNotificationOutboxMessageByIdQuery, ErrorOr<NotificationOutboxMessageDetailDto>>
{
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly IApplicationRepository _applicationRepository;

    public GetNotificationOutboxMessageByIdQueryHandler(
        INotificationOutboxRepository outboxRepository,
        IApplicationRepository applicationRepository)
    {
        _outboxRepository = outboxRepository;
        _applicationRepository = applicationRepository;
    }

    public async Task<ErrorOr<NotificationOutboxMessageDetailDto>> Handle(
        GetNotificationOutboxMessageByIdQuery request,
        CancellationToken cancellationToken)
    {
        var message = await _outboxRepository.GetByIdAsync(request.MessageId, cancellationToken);
        if (message is null)
        {
            return NotificationErrors.OutboxMessageNotFound(request.MessageId);
        }

        string? applicationName = null;
        if (message.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return new NotificationOutboxMessageDetailDto
        {
            Id = message.Id,
            NotificationTypeCode = message.NotificationTypeCode,
            Channel = message.Channel.ToString(),
            ApplicationId = message.ApplicationId,
            ApplicationName = applicationName,
            Recipient = message.Recipient,
            RecipientName = message.RecipientName,
            RecipientUserId = message.RecipientUserId,
            LanguageCode = message.LanguageCode,
            TemplateId = message.TemplateId,
            TemplateVersionId = message.TemplateVersionId,
            TemplateVersionNumber = message.TemplateVersionNumber,
            Subject = message.Subject,
            Status = message.Status.ToString(),
            AttemptCount = message.AttemptCount,
            NextAttemptAt = message.NextAttemptAt,
            SentAt = message.SentAt,
            LastError = message.LastError,
            CreatedAt = message.CreatedAt,
            CreatedBy = message.CreatedBy,
            BodyHtml = message.BodyHtml,
            BodyText = message.BodyText,
            ClaimedAt = message.ClaimedAt
        };
    }
}
