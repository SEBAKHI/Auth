using ErrorOr;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>Everything a page's rendered design depends on.</summary>
/// <param name="DesignId">The design the page shows.</param>
/// <param name="ContentHash">The design's fingerprint: its content and strings.</param>
/// <param name="Language">The normalised language it renders in.</param>
/// <param name="ApplicationName">What <c>App.Name</c> reads.</param>
/// <param name="PlatformName">What <c>Platform.Name</c> reads.</param>
public sealed record LoginDesignRenderKey(
    Guid DesignId,
    string ContentHash,
    string Language,
    string ApplicationName,
    string PlatformName);

/// <summary>
/// Keeps the designs anonymous sign-in pages are served, rendered, in memory.
/// </summary>
/// <remarks>
/// The render endpoints answer anyone, and a design is Liquid: without this, every visitor
/// request would run the whole design again. The key holds everything the output depends on,
/// so a save, an activation or a rename reaches the next request without any eviction.
/// </remarks>
public interface ILoginDesignRenderCache
{
    /// <summary>
    /// The kept outcome for <paramref name="key"/>, a refusal included; otherwise
    /// <paramref name="render"/>'s, kept for the next request.
    /// </summary>
    ErrorOr<string> GetOrRender(LoginDesignRenderKey key, Func<ErrorOr<string>> render);
}
