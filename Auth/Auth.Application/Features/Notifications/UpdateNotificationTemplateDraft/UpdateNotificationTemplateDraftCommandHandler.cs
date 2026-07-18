using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UpdateNotificationTemplateDraft;

/// <summary>
/// Handler for saving draft edits. Every changed subject/body is parsed by the
/// Liquid renderer first so syntax errors surface inline at save time, not at
/// publish time.
/// </summary>
public class UpdateNotificationTemplateDraftCommandHandler
    : IRequestHandler<UpdateNotificationTemplateDraftCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ITemplateRenderer _renderer;
    private readonly ILogger<UpdateNotificationTemplateDraftCommandHandler> _logger;

    public UpdateNotificationTemplateDraftCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        ITemplateRenderer renderer,
        ILogger<UpdateNotificationTemplateDraftCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _renderer = renderer;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        UpdateNotificationTemplateDraftCommand request,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
        {
            return NotificationErrors.TemplateNotFound(request.TemplateId);
        }

        // Optimistic concurrency: the editor sends the ModifiedAt it loaded with.
        if (request.ExpectedModifiedAt is { } expected &&
            template.ModifiedAt is { } actual &&
            Math.Abs((actual - expected).TotalMilliseconds) > 1)
        {
            return NotificationErrors.ConcurrencyConflict;
        }

        // Syntax gate: reject broken Liquid before it can reach the draft.
        foreach (var translation in request.Translations)
        {
            foreach (var source in new[] { translation.Subject, translation.BodyHtml, translation.BodyText })
            {
                if (string.IsNullOrEmpty(source))
                {
                    continue;
                }

                var validation = _renderer.Validate(source);
                if (validation.IsError)
                {
                    return validation.Errors;
                }
            }
        }

        foreach (var translation in request.Translations)
        {
            var result = template.UpsertTranslation(
                translation.LanguageCode,
                translation.Subject,
                translation.BodyHtml,
                translation.BodyText,
                request.ModifiedBy);
            if (result.IsError)
            {
                return result.Errors;
            }
        }

        foreach (var language in request.RemoveLanguages ?? [])
        {
            var result = template.RemoveTranslation(language, request.ModifiedBy);
            if (result.IsError)
            {
                return result.Errors;
            }
        }

        if (request.ChangeNote is not null)
        {
            var noteResult = template.SetDraftChangeNote(request.ChangeNote, request.ModifiedBy);
            if (noteResult.IsError)
            {
                return noteResult.Errors;
            }
        }

        await _templateRepository.UpdateAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template draft saved: {TemplateId} v{DraftVersion} ({TranslationCount} translations)",
            template.Id, template.DraftVersion?.VersionNumber, template.DraftVersion?.Translations.Count);

        return await BuildDetailAsync(template.Id, cancellationToken);
    }

    private async Task<ErrorOr<NotificationTemplateDetailDto>> BuildDetailAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdAsync(templateId, cancellationToken);
        if (template is null)
        {
            return NotificationErrors.TemplateNotFound(templateId);
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
