using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// A font file as stored: its relative key (composed into a public URL by
/// <see cref="IImageUrlComposer"/>, as images are) and the family name it declares.
/// </summary>
/// <param name="Key">The relative storage key, <c>fonts/{sha-256}.woff2</c>.</param>
/// <param name="FamilyName">The typographic or legacy family name in the font's name table; null when it has none.</param>
public sealed record StoredFont(string Key, string? FamilyName);

/// <summary>
/// Stores and removes the platform's font files. Unlike images, a font is never re-encoded:
/// the file is kept byte for byte, after a structural check that it is a WOFF2 font whose
/// data decompresses to what it declares. Files sit in the uploads volume under
/// <c>fonts/</c>, named by the hash of their content, and are served from there with the
/// headers a cross-origin <c>@font-face</c> needs.
/// </summary>
public interface IFontStorageService
{
    /// <summary>
    /// Checks and stores a WOFF2 file. <c>Font.Invalid</c> when it is not one, or is damaged;
    /// <c>Font.StorageUnavailable</c> when the volume cannot be written. The same content
    /// uploaded twice is one file.
    /// </summary>
    Task<ErrorOr<StoredFont>> SaveAsync(Stream content, CancellationToken cancellationToken);

    /// <summary>Deletes a stored font file by key (best-effort; a missing file is ignored).</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every font file not in <paramref name="keep"/> that was written before
    /// <paramref name="writtenBefore"/>: what a save left behind when it stopped between
    /// writing its file and recording it. Best-effort.
    /// </summary>
    Task PruneAsync(IReadOnlyCollection<string> keep, DateTime writtenBefore, CancellationToken cancellationToken);
}
