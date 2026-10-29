using System.Globalization;
using System.Text.Encodings.Web;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using ErrorOr;
using Fluid;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The renderer as it was before every render was bounded: Fluid's own parser and its step
/// limit, nothing else. Tests render legitimate content with it and with the bounded renderer
/// and require the same text, so the bounds can only ever refuse, never change, what a
/// legitimate template produces. Never use it for anything else.
/// </summary>
internal sealed class UnboundedTemplateRenderer : ITemplateRenderer, ITemplateRenderSession
{
    private static readonly FluidParser Parser = new();

    public ErrorOr<Success> Validate(string source) =>
        Parser.TryParse(source, out _, out var error) ? Result.Success : NotificationErrors.InvalidTemplateSyntax(error);

    public ITemplateRenderSession BeginSession(TemplateRenderLimits limits) => this;

    public ErrorOr<string> Render(
        string source, IReadOnlyDictionary<string, object?> model, string languageCode, bool encodeHtml)
    {
        if (!Parser.TryParse(source, out var template, out var error))
        {
            return NotificationErrors.InvalidTemplateSyntax(error);
        }

        var context = new TemplateContext(
            model.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new TemplateOptions { MaxSteps = 5_000, CultureInfo = CultureInfo.GetCultureInfo(languageCode) });
        return template.Render(context, encodeHtml ? HtmlEncoder.Default : NullEncoder.Default);
    }

    public ErrorOr<string> RenderTracking(
        string source,
        IReadOnlyDictionary<string, object?> model,
        string languageCode,
        bool encodeHtml,
        out IReadOnlyList<string> unresolvedVariables)
    {
        unresolvedVariables = [];
        return Render(source, model, languageCode, encodeHtml);
    }
}
