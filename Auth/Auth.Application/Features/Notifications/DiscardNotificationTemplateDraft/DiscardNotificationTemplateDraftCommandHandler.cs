using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.DiscardNotificationTemplateDraft;

/// <summary>
/// Handler for discarding the pending draft version.
/// </summary>
public class DiscardNotificationTemplateDraftCommandHandler
    : IRequestHandler<DiscardNotificationTemplateDraftCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<DiscardNotificationTemplateDraftCommandHandler> _logger;

    public DiscardNotificationTemplateDraftCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        ILogger<DiscardNotificationTemplateDraftCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        DiscardNotificationTemplateDraftCommand request,
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

        var result = template.DiscardDraft(request.DiscardedBy);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _templateRepository.UpdateAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template draft discarded: {TemplateId} by {DiscardedBy}",
            template.Id, request.DiscardedBy);

        string? applicationName = null;
        if (template.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
