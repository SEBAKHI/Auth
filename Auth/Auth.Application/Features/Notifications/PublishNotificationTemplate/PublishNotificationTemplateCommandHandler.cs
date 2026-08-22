using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PublishNotificationTemplate;

/// <summary>
/// Handler for publishing a template draft: validates every draft translation by
/// rendering it against the type's sample data (with unknown-variable tracking),
/// moves the published pointer, evicts the send-path cache, and dispatches the
/// published domain event for auditing.
/// </summary>
public class PublishNotificationTemplateCommandHandler
    : IRequestHandler<PublishNotificationTemplateCommand, ErrorOr<NotificationTemplateDetailDto>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly INotificationRenderer _renderer;
    private readonly ITemplateCacheInvalidator _cacheInvalidator;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<PublishNotificationTemplateCommandHandler> _logger;

    public PublishNotificationTemplateCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        IApplicationRepository applicationRepository,
        INotificationRenderer renderer,
        ITemplateCacheInvalidator cacheInvalidator,
        IDomainEventDispatcher eventDispatcher,
        ILogger<PublishNotificationTemplateCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _applicationRepository = applicationRepository;
        _renderer = renderer;
        _cacheInvalidator = cacheInvalidator;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<NotificationTemplateDetailDto>> Handle(
        PublishNotificationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await _templateRepository.GetByIdAsync(request.TemplateId, cancellationToken);
        if (template is null)
        {
            return NotificationErrors.TemplateNotFound(request.TemplateId);
        }

        var targetResult = template.ValidatePublishTarget(
            request.ExpectedDraftVersionId,
            request.ExpectedRevisionAt);
        if (targetResult.IsError)
        {
            return targetResult.Errors;
        }

        var type = await _typeRepository.GetByIdAsync(template.NotificationTypeId, cancellationToken);
        if (type is null)
        {
            return NotificationErrors.TypeNotFound(template.NotificationTypeId);
        }

        if (template.DraftVersion is not { } draft)
        {
            return NotificationErrors.NoDraftToPublish;
        }

        // Publish gate: render EVERY draft translation with sample data; syntax
        // errors and unknown variables block the publish for all languages.
        var sampleData = NotificationMapping.ParseSampleData(type.SampleDataJson);
        foreach (var translation in draft.Translations)
        {
            var rendered = await _renderer.RenderContentAsync(
                new NotificationContentRenderRequest
                {
                    Channel = template.Channel,
                    ApplicationId = template.ApplicationId,
                    LanguageCode = translation.LanguageCode,
                    Subject = translation.Subject,
                    BodyHtml = translation.BodyHtml,
                    BodyText = translation.BodyText,
                    Variables = sampleData,
                    FailOnUnknownVariables = true
                },
                cancellationToken);

            if (rendered.IsError)
            {
                // Prefix the language so the editor can point at the failing tab.
                return Error.Validation(
                    code: rendered.FirstError.Code,
                    description: $"[{translation.LanguageCode}] {rendered.FirstError.Description}",
                    metadata: rendered.FirstError.Metadata is null ? null : new(rendered.FirstError.Metadata));
            }
        }

        var publishResult = template.Publish(
            request.ExpectedDraftVersionId,
            request.ExpectedRevisionAt,
            request.PublishedBy);
        if (publishResult.IsError)
        {
            return publishResult.Errors;
        }

        var persisted = await _templateRepository.TryPublishAsync(
            template,
            request.ExpectedDraftVersionId,
            request.ExpectedRevisionAt,
            cancellationToken);
        if (!persisted)
        {
            return NotificationErrors.PublishTargetChanged;
        }

        _cacheInvalidator.InvalidateTemplate(type.Code, template.Channel, template.ApplicationId);
        await _eventDispatcher.DispatchEventsAsync(template, cancellationToken);

        _logger.LogInformation(
            "Notification template published: {TemplateId} (type {TypeCode}) v{Version} by {PublishedBy}",
            template.Id, type.Code, template.PublishedVersion?.VersionNumber, request.PublishedBy);

        string? applicationName = null;
        if (template.ApplicationId is { } applicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
            applicationName = application?.Name;
        }

        return NotificationMapping.ToDetailDto(template, type, applicationName);
    }
}
