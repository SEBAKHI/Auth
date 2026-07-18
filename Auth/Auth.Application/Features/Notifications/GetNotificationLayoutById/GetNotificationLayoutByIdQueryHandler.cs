using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationLayoutById;

/// <summary>
/// Handler returning one notification layout for editing.
/// </summary>
public class GetNotificationLayoutByIdQueryHandler
    : IRequestHandler<GetNotificationLayoutByIdQuery, ErrorOr<NotificationLayoutDto>>
{
    private readonly INotificationLayoutRepository _layoutRepository;
    private readonly IApplicationRepository _applicationRepository;

    public GetNotificationLayoutByIdQueryHandler(
        INotificationLayoutRepository layoutRepository,
        IApplicationRepository applicationRepository)
    {
        _layoutRepository = layoutRepository;
        _applicationRepository = applicationRepository;
    }

    public async Task<ErrorOr<NotificationLayoutDto>> Handle(
        GetNotificationLayoutByIdQuery request,
        CancellationToken cancellationToken)
    {
        var layout = await _layoutRepository.GetByIdAsync(request.LayoutId, cancellationToken);
        if (layout is null)
        {
            return NotificationErrors.LayoutNotFound(request.LayoutId);
        }

        string? applicationName = null;
        if (layout.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToLayoutDto(layout, applicationName);
    }
}
