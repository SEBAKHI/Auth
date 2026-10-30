using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Renders and validates Liquid template sources. Implementations must be
/// sandboxed: model-dictionary member access only, bounded execution, and
/// HTML-encoding of variables in HTML contexts.
/// </summary>
/// <remarks>
/// Rendering happens only inside a session started with <see cref="BeginSession"/>, so every
/// render runs under the limits of its purpose: a render cannot be asked for without them.
/// </remarks>
public interface ITemplateRenderer
{
    /// <summary>
    /// Parses the source and reports syntax errors, or nesting too deep to render
    /// (<c>Template.TooComplex</c>), without rendering.
    /// </summary>
    ErrorOr<Success> Validate(string source);

    /// <summary>
    /// Starts one render operation (one email, one sign-in page in one language): every render
    /// made through the session shares one budget of work and time, and each one's output is
    /// capped at <see cref="TemplateRenderLimits.MaxOutputBytes"/>.
    /// </summary>
    ITemplateRenderSession BeginSession(TemplateRenderLimits limits);
}
