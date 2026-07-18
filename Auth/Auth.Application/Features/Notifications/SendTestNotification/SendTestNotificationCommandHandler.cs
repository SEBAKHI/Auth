using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.SendTestNotification;

/// <summary>
/// Handler for test sends: renders the chosen version's translation with sample
/// data and delivers it through the real channel strategy.
/// </summary>
public class SendTestNotificationCommandHandler
    : IRequestHandler<SendTestNotificationCommand, ErrorOr<Success>>
{
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationTypeRepository _typeRepository;
    private readonly INotificationRenderer _renderer;
    private readonly INotificationChannelFactory _channelFactory;
    private readonly ILogger<SendTestNotificationCommandHandler> _logger;

    public SendTestNotificationCommandHandler(
        INotificationTemplateRepository templateRepository,
        INotificationTypeRepository typeRepository,
        INotificationRenderer renderer,
        INotificationChannelFactory channelFactory,
        ILogger<SendTestNotificationCommandHandler> logger)
    {
        _templateRepository = templateRepository;
        _typeRepository = typeRepository;
        _renderer = renderer;
        _channelFactory = channelFactory;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        SendTestNotificationCommand request,
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

        var version = request.VersionId is { } versionId
            ? template.Versions.FirstOrDefault(v => v.Id == versionId)
            : template.DraftVersion ?? template.PublishedVersion;
        if (version is null)
        {
            return request.VersionId is { } missing
                ? NotificationErrors.VersionNotFound(missing)
                : NotificationErrors.NotPublished;
        }

        var translation =
            version.Translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, request.LanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? version.Translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, template.DefaultLanguage, StringComparison.OrdinalIgnoreCase));
        if (translation is null)
        {
            return NotificationErrors.TranslationNotFound(request.LanguageCode);
        }

        var rendered = await _renderer.RenderContentAsync(
            new NotificationContentRenderRequest
            {
                Channel = template.Channel,
                ApplicationId = template.ApplicationId,
                LanguageCode = translation.LanguageCode,
                Subject = translation.Subject,
                BodyHtml = translation.BodyHtml,
                BodyText = translation.BodyText,
                Variables = NotificationMapping.ParseSampleData(type.SampleDataJson)
            },
            cancellationToken);

        if (rendered.IsError)
        {
            return rendered.Errors;
        }

        var channel = _channelFactory.GetChannel(template.Channel);
        if (channel is null)
        {
            return NotificationErrors.ChannelNotSupported(template.Channel.ToString());
        }

        var sendResult = await channel.SendAsync(
            rendered.Value with { RecipientAddress = request.RecipientEmail },
            cancellationToken);

        if (!sendResult.IsError)
        {
            _logger.LogInformation(
                "Test notification sent: template {TemplateId} v{Version} [{Language}] to {Recipient} by {RequestedBy}",
                template.Id, version.VersionNumber, translation.LanguageCode,
                request.RecipientEmail, request.RequestedBy);
        }

        return sendResult;
    }
}
