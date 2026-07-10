using ErrorOr;

namespace Auth.Application.Interfaces;

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
}
