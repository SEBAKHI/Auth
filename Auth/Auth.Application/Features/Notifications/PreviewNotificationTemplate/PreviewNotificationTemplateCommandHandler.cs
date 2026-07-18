using Auth.Application.DTOs;
using Auth.Application.Features.Notifications.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PreviewNotificationTemplate;

/// <summary>
/// Handler rendering an editor-buffer preview server-side, guaranteeing pixel
/// parity with real sends (same Fluid renderer, same layout composition).
/// </summary>
public class PreviewNotificationTemplateCommandHandler
    : IRequestHandler<PreviewNotificationTemplateCommand, ErrorOr<NotificationPreviewDto>>
{
    private readonly INotificationTypeRepository _typeRepository;
    private readonly INotificationRenderer _renderer;

    public PreviewNotificationTemplateCommandHandler(
        INotificationTypeRepository typeRepository,
        INotificationRenderer renderer)
    {
        _typeRepository = typeRepository;
        _renderer = renderer;
    }

    public async Task<ErrorOr<NotificationPreviewDto>> Handle(
        PreviewNotificationTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var type = await _typeRepository.GetByIdAsync(request.NotificationTypeId, cancellationToken);
        if (type is null)
        {
            return NotificationErrors.TypeNotFound(request.NotificationTypeId);
        }

        Dictionary<string, object?> variables;
        try
        {
            variables = NotificationMapping.ParseSampleData(type.SampleDataJson, request.SampleOverridesJson);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return NotificationErrors.RenderFailed($"Invalid sample data JSON: {ex.Message}");
        }

        var rendered = await _renderer.RenderContentAsync(
            new NotificationContentRenderRequest
            {
                Channel = request.Channel,
                ApplicationId = request.ApplicationId,
                LanguageCode = request.LanguageCode,
                Subject = request.Subject,
                BodyHtml = request.BodyHtml,
                BodyText = request.BodyText,
                Variables = variables
            },
            cancellationToken);

        if (rendered.IsError)
        {
            return rendered.Errors;
        }

        return new NotificationPreviewDto
        {
            Subject = rendered.Value.Subject,
            Html = rendered.Value.BodyHtml,
            Text = rendered.Value.BodyText,
            LanguageCode = rendered.Value.LanguageCode,
            Direction = Languages.GetDirection(rendered.Value.LanguageCode)
        };
    }
}
