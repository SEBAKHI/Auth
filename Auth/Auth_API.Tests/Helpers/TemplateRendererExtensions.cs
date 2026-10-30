using Auth.Application.Interfaces;
using ErrorOr;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// One render in a session of its own, under the notification limits: what a test of one
/// template's output needs, without spelling out the session each time.
/// </summary>
internal static class TemplateRendererExtensions
{
    public static ErrorOr<string> Render(
        this ITemplateRenderer renderer,
        string source,
        IReadOnlyDictionary<string, object?> model,
        string languageCode,
        bool encodeHtml) =>
        renderer.BeginSession(TemplateRenderLimits.Notification).Render(source, model, languageCode, encodeHtml);

    public static ErrorOr<string> RenderTracking(
        this ITemplateRenderer renderer,
        string source,
        IReadOnlyDictionary<string, object?> model,
        string languageCode,
        bool encodeHtml,
        out IReadOnlyList<string> unresolvedVariables) =>
        renderer.BeginSession(TemplateRenderLimits.Notification)
            .RenderTracking(source, model, languageCode, encodeHtml, out unresolvedVariables);
}
