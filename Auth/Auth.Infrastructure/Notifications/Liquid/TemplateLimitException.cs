namespace Auth.Infrastructure.Notifications.Liquid;

/// <summary>Which limit a render crossed.</summary>
internal enum TemplateLimit
{
    /// <summary>The output passed its cap in UTF-8 bytes.</summary>
    OutputTooLarge,

    /// <summary>Nesting, render time, the work budget or the size of one value.</summary>
    TooComplex
}

/// <summary>
/// Thrown from inside Fluid when a render or a parse crosses a limit, to unwind it at once.
/// The renderer catches it and returns the matching domain error; it never leaves the renderer.
/// </summary>
internal sealed class TemplateLimitException(TemplateLimit limit)
    : Exception($"The template render crossed its {limit} limit.")
{
    public TemplateLimit Limit { get; } = limit;
}
