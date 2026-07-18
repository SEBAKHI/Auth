using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.CreateNotificationTemplate;

/// <summary>
/// Handler for creating a notification template with an empty draft version 1.
/// </summary>
public class CreateNotificationTemplateCommandHandler
    : IRequestHandler<CreateNotificationTemplateCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<CreateNotificationTemplateCommandHandler> _logger;

    public CreateNotificationTemplateCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        ILogger<CreateNotificationTemplateCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        CreateNotificationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var type = await _typeRepository.GetByIdAsync(request.NotificationTypeId, cancellationToken);
        if (type is null)
        {
            return NotificationErrors.TypeNotFound(request.NotificationTypeId);
        }

        string? applicationName = null;
        if (request.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            if (application is null)
            {
                return ApplicationErrors.NotFound(applicationId);
            }

            applicationName = application.Name;
        }

        var exists = await _templateRepository.ExistsAsync(
            request.NotificationTypeId, request.ApplicationId, request.Channel, cancellationToken);
        if (exists)
        {
            return NotificationErrors.DuplicateTemplate;
        }

        var templateResult = NotificationTemplate.Create(
            request.NotificationTypeId,
            request.ApplicationId,
            request.Channel,
            request.DefaultLanguage,
            request.CreatedBy);
        if (templateResult.IsError)
        {
            return templateResult.Errors;
        }

        var template = templateResult.Value;
        await _templateRepository.CreateAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template created: {TemplateId} (type {TypeCode}, application {ApplicationId}, channel {Channel})",
            template.Id, type.Code, template.ApplicationId, template.Channel);

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
