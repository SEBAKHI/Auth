using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationOutboxMessages;

/// <summary>
/// Handler for the paged notification delivery log.
/// </summary>
public class GetNotificationOutboxMessagesQueryHandler
    : IRequestHandler<GetNotificationOutboxMessagesQuery, ErrorOr<PagedNotificationOutboxDto>>
{
    private readonly INotificationOutboxRepository _outboxRepository;

    public GetNotificationOutboxMessagesQueryHandler(INotificationOutboxRepository outboxRepository)
    {
        _outboxRepository = outboxRepository;
    }

    public async Task<ErrorOr<PagedNotificationOutboxDto>> Handle(
        GetNotificationOutboxMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _outboxRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.Status,
            request.Channel,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        return new PagedNotificationOutboxDto
        {
            Messages = items.Select(item => new NotificationOutboxMessageDto
            {
                Id = item.Id,
                NotificationTypeCode = item.NotificationTypeCode,
                Channel = ((NotificationChannelType)item.Channel).ToString(),
                ApplicationId = item.ApplicationId,
                ApplicationName = item.ApplicationName,
                Recipient = item.Recipient,
                RecipientName = item.RecipientName,
                RecipientUserId = item.RecipientUserId,
                LanguageCode = item.LanguageCode,
                TemplateId = item.TemplateId,
                TemplateVersionId = item.TemplateVersionId,
                TemplateVersionNumber = item.TemplateVersionNumber,
                Subject = item.Subject,
                Status = ((NotificationDeliveryStatus)item.Status).ToString(),
                AttemptCount = item.AttemptCount,
                NextAttemptAt = item.NextAttemptAt,
                SentAt = item.SentAt,
                LastError = item.LastError,
                CreatedAt = item.CreatedAt,
                CreatedBy = item.CreatedBy
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
