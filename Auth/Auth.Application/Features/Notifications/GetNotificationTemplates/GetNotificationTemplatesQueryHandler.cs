using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTemplates;

/// <summary>
/// Handler for the paged admin list of notification templates.
/// </summary>
public class GetNotificationTemplatesQueryHandler
    : IRequestHandler<GetNotificationTemplatesQuery, ErrorOr<PagedNotificationTemplatesDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;

    public GetNotificationTemplatesQueryHandler(INotificationTemplateRepository templateRepository)
    {
        _templateRepository = templateRepository;
    }

    public async Task<ErrorOr<PagedNotificationTemplatesDto>> Handle(
        GetNotificationTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _templateRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.NotificationTypeId,
            request.ApplicationId,
            request.Channel,
            request.IsPublished,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        return new PagedNotificationTemplatesDto
        {
            Templates = items.Select(item => new NotificationTemplateDto
            {
                Id = item.Id,
                NotificationTypeId = item.NotificationTypeId,
                TypeCode = item.TypeCode,
                TypeName = item.TypeName,
                TypeIsSystem = item.TypeIsSystem,
                ApplicationId = item.ApplicationId,
                ApplicationName = item.ApplicationName,
                Channel = ((NotificationChannelType)item.Channel).ToString(),
                DefaultLanguage = item.DefaultLanguage,
                IsPublished = item.PublishedVersionId is not null,
                PublishedVersionNumber = item.PublishedVersionNumber,
                HasDraft = item.DraftVersionId is not null,
                DraftVersionNumber = item.DraftVersionNumber,
                TranslationCount = item.TranslationCount,
                CreatedAt = item.CreatedAt,
                ModifiedAt = item.ModifiedAt
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
