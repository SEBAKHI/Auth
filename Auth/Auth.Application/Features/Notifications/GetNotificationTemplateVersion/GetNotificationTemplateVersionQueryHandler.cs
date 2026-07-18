using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTemplateVersion;

/// <summary>
/// Handler returning one version's full translations.
/// </summary>
public class GetNotificationTemplateVersionQueryHandler
    : IRequestHandler<GetNotificationTemplateVersionQuery, ErrorOr<NotificationTemplateVersionDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;

    public GetNotificationTemplateVersionQueryHandler(INotificationTemplateRepository templateRepository)
    {
        _templateRepository = templateRepository;
    }

    public async Task<ErrorOr<NotificationTemplateVersionDto>> Handle(
        GetNotificationTemplateVersionQuery request,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
        {
            return NotificationErrors.TemplateNotFound(request.TemplateId);
        }

        var version = template.Versions.FirstOrDefault(v => v.Id == request.VersionId);
        if (version is null)
        {
            return NotificationErrors.VersionNotFound(request.VersionId);
        }

        return NotificationMapping.ToVersionDto(version);
    }
}
