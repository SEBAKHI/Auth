using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.DeleteNotificationTemplate;

/// <summary>
/// Handler for deleting a notification template (system-global protected).
/// </summary>
public class DeleteNotificationTemplateCommandHandler
    : IRequestHandler<DeleteNotificationTemplateCommand, ErrorOr<Deleted>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly ITemplateCacheInvalidator _cacheInvalidator;
    private readonly ILogger<DeleteNotificationTemplateCommandHandler> _logger;

    public DeleteNotificationTemplateCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        ITemplateCacheInvalidator cacheInvalidator,
        ILogger<DeleteNotificationTemplateCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(
        DeleteNotificationTemplateCommand request,
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

        var deletable = template.EnsureDeletable(type.IsSystem);
        if (deletable.IsError)
        {
            return deletable.Errors;
        }

        await _templateRepository.DeleteAsync(template.Id, cancellationToken);
        _cacheInvalidator.InvalidateTemplate(type.Code, template.Channel, template.ApplicationId);

        _logger.LogInformation(
            "Notification template deleted: {TemplateId} (type {TypeCode}, application {ApplicationId}) by {DeletedBy}",
            template.Id, type.Code, template.ApplicationId, request.DeletedBy);

        return Result.Deleted;
    }
}
