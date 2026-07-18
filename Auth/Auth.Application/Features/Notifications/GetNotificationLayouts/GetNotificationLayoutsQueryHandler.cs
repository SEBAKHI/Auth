using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationLayouts;

/// <summary>
/// Handler returning all notification layouts with application names resolved
/// in one batch (no N+1).
/// </summary>
public class GetNotificationLayoutsQueryHandler
    : IRequestHandler<GetNotificationLayoutsQuery, ErrorOr<List<NotificationLayoutDto>>>
{
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IApplicationRepository _applicationRepository;

    public GetNotificationLayoutsQueryHandler(
        INotificationLayoutRepository layoutRepository,
        IApplicationRepository applicationRepository)
    {
        _layoutRepository = layoutRepository;
        _applicationRepository = applicationRepository;
    }

    public async Task<ErrorOr<List<NotificationLayoutDto>>> Handle(
        GetNotificationLayoutsQuery request,
        CancellationToken cancellationToken)
    {
        var layouts = await _layoutRepository.GetAllAsync(cancellationToken);

        var applicationNames = new Dictionary<Guid, string>();
        foreach (var applicationId in layouts
                     .Where(l => l.ApplicationId is not null)
                     .Select(l => l.ApplicationId!.Value)
                     .Distinct())
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            if (application is not null)
            {
                applicationNames[applicationId] = application.Name;
            }
        }

        return layouts
            .Select(layout => NotificationMapping.ToLayoutDto(
                layout,
                layout.ApplicationId is { } appId && applicationNames.TryGetValue(appId, out var name)
                    ? name
                    : null))
            .ToList();
    }
}
