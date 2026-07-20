using Auth.Application.DTOs;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationsSummary;

/// <summary>
/// Composes the notifications overview from the three aggregates that make up
/// the section. Each is read through its own repository — no cross-aggregate
/// SQL — and the counts that matter are computed where they belong: templates
/// and layouts are few and bounded by their unique scopes, so they are counted
/// in memory, while the delivery log grows without bound and is counted in SQL.
/// </summary>
public class GetNotificationsSummaryQueryHandler
    : IRequestHandler<GetNotificationsSummaryQuery, ErrorOr<NotificationsSummaryDto>>
{
    /// <summary>
    /// Upper bound for the template read. One template exists per
    /// (application, type, channel) scope, so this cannot be reached in
    /// practice; it only stops the overview degrading if it ever were.
    /// </summary>
    private const int TemplateScanLimit = 500;

    /// <summary>How many published templates the overview lists.</summary>
    private const int PublishedTemplatePreviewCount = 8;

    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly IApplicationRepository _applicationRepository;

    public GetNotificationsSummaryQueryHandler(
        INotificationTemplateRepository templateRepository,
        INotificationLayoutRepository layoutRepository,
        INotificationOutboxRepository outboxRepository,
        IApplicationRepository applicationRepository)
    {
        _templateRepository = templateRepository;
        _layoutRepository = layoutRepository;
        _outboxRepository = outboxRepository;
        _applicationRepository = applicationRepository;
    }

    public async Task<ErrorOr<NotificationsSummaryDto>> Handle(
        GetNotificationsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var (templates, templateCount) = await _templateRepository.GetPagedAsync(
            pageNumber: 1,
            pageSize: TemplateScanLimit,
            notificationTypeId: null,
            applicationId: null,
            channel: null,
            isPublished: null,
            searchTerm: null,
            sortBy: null,
            sortDirection: SortDirection.Asc,
            cancellationToken);

        var layouts = await _layoutRepository.GetAllAsync(cancellationToken);
        var outbox = await _outboxRepository.GetStatsAsync(cancellationToken);

        var applicationNames = await ResolveApplicationNamesAsync(
            layouts.Where(l => l.ApplicationId is not null).Select(l => l.ApplicationId!.Value),
            cancellationToken);

        return new NotificationsSummaryDto
        {
            Templates = new NotificationTemplatesSummaryDto
            {
                Total = templateCount,
                Published = templates.Count(t => t.PublishedVersionId is not null),
                Drafts = templates.Count(t => t.DraftVersionId is not null),
                ByChannel = templates
                    .GroupBy(t => ((NotificationChannelType)t.Channel).ToString())
                    .ToDictionary(group => group.Key, group => group.Count())
            },
            Layouts = new NotificationLayoutsSummaryDto
            {
                Total = layouts.Count,
                Published = layouts.Count(layout => layout.IsPublished)
            },
            Outbox = new NotificationOutboxSummaryDto
            {
                Total = outbox.Total,
                Pending = outbox.Pending,
                Sent = outbox.Sent,
                Failed = outbox.Failed,
                Last24Hours = outbox.Last24Hours
            },
            PublishedTemplates = templates
                .Where(t => t.PublishedVersionId is not null)
                .OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt)
                .Take(PublishedTemplatePreviewCount)
                .Select(t => new PublishedNotificationTemplateDto
                {
                    Id = t.Id,
                    TypeCode = t.TypeCode,
                    TypeName = t.TypeName,
                    ApplicationName = t.ApplicationName,
                    Channel = ((NotificationChannelType)t.Channel).ToString(),
                    PublishedVersionNumber = t.PublishedVersionNumber,
                    HasUnpublishedDraft = t.DraftVersionId is not null,
                    ModifiedAt = t.ModifiedAt
                })
                .ToList(),
            PublishedLayouts = layouts
                .Select(layout => new PublishedNotificationLayoutDto
                {
                    Id = layout.Id,
                    Name = layout.Name,
                    ApplicationName = layout.ApplicationId is { } appId
                        && applicationNames.TryGetValue(appId, out var name)
                            ? name
                            : null,
                    Channel = layout.Channel.ToString(),
                    IsPublished = layout.IsPublished,
                    HasUnpublishedChanges = layout.HasUnpublishedChanges,
                    PublishedAt = layout.PublishedAt
                })
                .ToList()
        };
    }

    private async Task<Dictionary<Guid, string>> ResolveApplicationNamesAsync(
        IEnumerable<Guid> applicationIds,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var applicationId in applicationIds.Distinct())
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            if (application is not null)
            {
                names[applicationId] = application.Name;
            }
        }

        return names;
    }
}
