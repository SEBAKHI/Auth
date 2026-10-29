using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// One render operation started by <see cref="ITemplateRenderer.BeginSession"/>. The parts of
/// a composition (subject, body, strings, layout) render through the same session, so together
/// they spend one budget: a template cannot multiply it by splitting itself into many parts.
/// A session is used by one caller at a time and is not thread-safe.
/// </summary>
/// <remarks>
/// A render over the limits returns <c>Template.OutputTooLarge</c> (its output passed the
/// cap) or <c>Template.TooComplex</c> (it nested too deeply, ran too long, or built too much),
/// and stops when it crosses them: the excess is never built.
/// </remarks>
public interface ITemplateRenderSession
{
    /// <summary>
    /// Renders a template source with the given model.
    /// </summary>
    /// <param name="source">The Liquid template source.</param>
    /// <param name="model">Variable values keyed by name.</param>
    /// <param name="languageCode">Culture used by culture-aware filters (e.g. date).</param>
    /// <param name="encodeHtml">
    /// When true, variable output is HTML-encoded (bodies); when false it is
    /// emitted raw (subjects, plain-text bodies).
    /// </param>
    ErrorOr<string> Render(
        string source,
        IReadOnlyDictionary<string, object?> model,
        string languageCode,
        bool encodeHtml);

    /// <summary>
    /// Renders the source against the model and additionally reports every
    /// variable the template references that the model does not supply
    /// (publish-time validation against the type's sample data).
    /// </summary>
    ErrorOr<string> RenderTracking(
        string source,
        IReadOnlyDictionary<string, object?> model,
        string languageCode,
        bool encodeHtml,
        out IReadOnlyList<string> unresolvedVariables);
}
