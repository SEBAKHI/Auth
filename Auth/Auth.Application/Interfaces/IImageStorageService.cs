using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>Which email surface a plated logo rendition is built for.</summary>
public enum EmailLogoVariant
{
    /// <summary>Plated for the light email card.</summary>
    Light = 0,

    /// <summary>Plated for the dark email card.</summary>
    Dark = 1
}

/// <summary>
/// A logo rendition that is safe to embed in an HTML email: an opaque PNG with the brand
/// mark centred on a baked-in plate, plus the CSS pixel size the layout must render it at.
/// </summary>
/// <remarks>
/// Both properties of that description are load-bearing and were learned from real-client
/// failures, not from theory:
/// <list type="bullet">
/// <item>PNG, never WebP — Gmail's backend transcodes WebP to JPEG, which has no alpha, so a
/// transparent mark is flattened onto black; Outlook for Windows cannot decode WebP at all.</item>
/// <item>The plate is baked into the raster, never applied with CSS — no mail client recolours
/// image pixels, but several force-invert declared background colours, which would turn a CSS
/// plate dark underneath an untouched dark mark.</item>
/// </list>
/// The explicit size exists because Outlook's Word engine ignores <c>height:auto</c>.
/// </remarks>
/// <param name="Key">Relative storage key of the rendition.</param>
/// <param name="Width">Intended CSS width in pixels.</param>
/// <param name="Height">Intended CSS height in pixels.</param>
public sealed record EmailLogoRendition(string Key, int Width, int Height);

/// <summary>
/// Stores and removes image files behind a swappable backend (filesystem today,
/// object storage later). The caller passes an already size-checked stream; the
/// service validates it is a real raster image, re-encodes/resizes/strips metadata,
/// writes it under a random key, and returns that relative key. The absolute URL is
/// composed at read time via <see cref="IImageUrlComposer"/>.
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// Processes and stores an uploaded image. Returns the relative storage key, or a
    /// validation error when the content type is unsupported or the bytes are not an image.
    /// </summary>
    Task<ErrorOr<string>> SaveImageAsync(Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Deletes a stored image by key (best-effort; missing files and absolute URLs are ignored).</summary>
    Task DeleteImageAsync(string? key, CancellationToken cancellationToken);

    /// <summary>
    /// Builds (or rebuilds) the email-safe rendition of a stored logo and returns it, or null
    /// when there is nothing to plate (no key, an externally hosted absolute URL, a missing or
    /// undecodable source file, or a storage volume that is not writable).
    /// </summary>
    /// <remarks>
    /// Admin- and startup-time only. Never call this on the send path: with
    /// <c>Notifications:UseOutbox</c> enabled the render runs inside the HTTP request that
    /// triggers the mail, and the uploads directory is not reliably writable on shared hosting.
    /// </remarks>
    Task<EmailLogoRendition?> EnsureEmailLogoRenditionAsync(
        string? sourceKey, EmailLogoVariant variant, CancellationToken cancellationToken);

    /// <summary>
    /// Read-only lookup of an already-built rendition. Returns null when it has not been built,
    /// which the caller must treat as "no logo" rather than falling back to the raw source —
    /// the raw source is the unsafe artifact this rendition exists to replace.
    /// </summary>
    Task<EmailLogoRendition?> GetEmailLogoRenditionAsync(
        string? sourceKey, EmailLogoVariant variant, CancellationToken cancellationToken);
}
