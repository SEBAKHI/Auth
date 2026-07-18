using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Renders and validates Liquid template sources. Implementations must be
/// sandboxed: model-dictionary member access only, bounded execution, and
/// HTML-encoding of variables in HTML contexts.
/// </summary>
public interface ITemplateRenderer
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
    /// Parses the source and reports syntax errors without rendering.
    /// </summary>
    ErrorOr<Success> Validate(string source);

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
