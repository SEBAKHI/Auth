using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.RollbackNotificationTemplate;

/// <summary>
/// Handler for rolling back to a previous version (pointer move; history preserved),
/// with cache eviction and audit event dispatch.
/// </summary>
public class RollbackNotificationTemplateCommandHandler
    : IRequestHandler<RollbackNotificationTemplateCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ITemplateCacheInvalidator _cacheInvalidator;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<RollbackNotificationTemplateCommandHandler> _logger;

    public RollbackNotificationTemplateCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        ITemplateCacheInvalidator cacheInvalidator,
        IDomainEventDispatcher eventDispatcher,
        ILogger<RollbackNotificationTemplateCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _cacheInvalidator = cacheInvalidator;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        RollbackNotificationTemplateCommand request,
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

        var result = template.RollbackTo(request.TargetVersionId, request.RolledBackBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _templateRepository.UpdateAsync(template, cancellationToken);

        _cacheInvalidator.InvalidateTemplate(type.Code, template.Channel, template.ApplicationId);
        await _eventDispatcher.DispatchEventsAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template rolled back: {TemplateId} (type {TypeCode}) to v{Version} by {RolledBackBy}",
            template.Id, type.Code, template.PublishedVersion?.VersionNumber, request.RolledBackBy);

        string? applicationName = null;
        if (template.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
