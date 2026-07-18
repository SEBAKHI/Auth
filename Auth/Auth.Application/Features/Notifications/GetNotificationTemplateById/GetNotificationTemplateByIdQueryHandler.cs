using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTemplateById;

/// <summary>
/// Handler for the full editor view of one notification template.
/// </summary>
public class GetNotificationTemplateByIdQueryHandler
    : IRequestHandler<GetNotificationTemplateByIdQuery, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;

    public GetNotificationTemplateByIdQueryHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        GetNotificationTemplateByIdQuery request,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
        {
            return NotificationErrors.TemplateNotFound(request.TemplateId);
        }

        var type = await _typeRepository.GetByIdAsync(template.NotificationTypeId, cancellationToken);
        if (type is null)
        {
            return NotificationErrors.TypeNotFound(template.NotificationTypeId);
        }

        string? applicationName = null;
        if (template.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
