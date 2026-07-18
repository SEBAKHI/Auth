using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PreviewNotificationLayout;

/// <summary>
/// Handler rendering a layout draft with representative placeholder content.
/// </summary>
public class PreviewNotificationLayoutCommandHandler
    : IRequestHandler<PreviewNotificationLayoutCommand, ErrorOr<NotificationPreviewDto>>
{
    private const string PlaceholderBody = """
        <div class="header">
            <h1>Sample Heading</h1>
        </div>
        <p class="message">This is placeholder body content so you can judge the layout chrome.</p>
        <div class="button-container">
            <a class="button" href="https://example.com">Sample Button</a>
        </div>
        <div class="warning">A sample warning block.</div>
        """;

    private readonly INotificationRenderer _renderer;

    public PreviewNotificationLayoutCommandHandler(INotificationRenderer renderer)
    {
        _renderer = renderer;
    }

    public async Task<ErrorOr<NotificationPreviewDto>> Handle(
        PreviewNotificationLayoutCommand request,
        CancellationToken cancellationToken)
    {
        var rendered = await _renderer.RenderContentAsync(
            new NotificationContentRenderRequest
            {
                LanguageCode = request.LanguageCode,
                Subject = "Layout preview",
                BodyHtml = PlaceholderBody,
                LayoutContentOverride = request.LayoutContent,
                LayoutStringsJsonOverride = request.LayoutStringsJson
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
