using Auth.Domain.Constants;

namespace Auth.Application.Interfaces;

/// <summary>
/// The limits of one render purpose. The output cap counts UTF-8 bytes after encoding, so it
/// is the size of what is sent or served; the renderer derives its work and time budget from it.
/// </summary>
/// <param name="MaxOutputBytes">Largest output of one render, in UTF-8 bytes.</param>
public sealed record TemplateRenderLimits(int MaxOutputBytes)
{
    /// <summary>Every notification render: subject, body, layout, text, SMS and push (100 KB).</summary>
    public static TemplateRenderLimits Notification { get; } = new(NotificationLimits.MaxRenderedBytes);

    /// <summary>Every sign-in page design render, in any language (256 KB).</summary>
    public static TemplateRenderLimits LoginDesign { get; } = new(Domain.Entities.LoginDesign.MaxRenderedBytes);
}
