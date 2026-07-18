using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.RestoreNotificationTemplateVersion;

/// <summary>
/// Handler for restoring a historical version as a new editable draft.
/// </summary>
public class RestoreNotificationTemplateVersionCommandHandler
    : IRequestHandler<RestoreNotificationTemplateVersionCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<RestoreNotificationTemplateVersionCommandHandler> _logger;

    public RestoreNotificationTemplateVersionCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        ILogger<RestoreNotificationTemplateVersionCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        RestoreNotificationTemplateVersionCommand request,
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

        var result = template.CreateDraftFromVersion(request.SourceVersionId, request.RestoredBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _templateRepository.UpdateAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template version restored as draft: {TemplateId} v{SourceVersion} -> v{DraftVersion}",
            template.Id, request.SourceVersionId, result.Value.VersionNumber);

        string? applicationName = null;
        if (template.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
