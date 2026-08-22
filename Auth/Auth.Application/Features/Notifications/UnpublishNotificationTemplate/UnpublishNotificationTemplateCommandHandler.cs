using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UnpublishNotificationTemplate;

/// <summary>
/// Handler for unpublishing a template (system-global templates are protected in
/// the domain), with cache eviction and audit event dispatch.
/// </summary>
public class UnpublishNotificationTemplateCommandHandler
    : IRequestHandler<UnpublishNotificationTemplateCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ITemplateCacheInvalidator _cacheInvalidator;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<UnpublishNotificationTemplateCommandHandler> _logger;

    public UnpublishNotificationTemplateCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        ITemplateCacheInvalidator cacheInvalidator,
        IDomainEventDispatcher eventDispatcher,
        ILogger<UnpublishNotificationTemplateCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _cacheInvalidator = cacheInvalidator;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        UnpublishNotificationTemplateCommand request,
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

        var result = template.Unpublish(
            type.IsSystem,
            request.ExpectedPublishedVersionId,
            request.UnpublishedBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        var persisted = await _templateRepository.TryUnpublishAsync(
            template,
            request.ExpectedPublishedVersionId,
            cancellationToken);
        if (!persisted)
        {
            return NotificationErrors.UnpublishTargetChanged;
        }

        _cacheInvalidator.InvalidateTemplate(type.Code, template.Channel, template.ApplicationId);
        await _eventDispatcher.DispatchEventsAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template unpublished: {TemplateId} (type {TypeCode}) by {UnpublishedBy}",
            template.Id, type.Code, request.UnpublishedBy);

        string? applicationName = null;
        if (template.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
