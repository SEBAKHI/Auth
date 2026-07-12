namespace Auth.Application.Interfaces;

/// <summary>
/// Composes an absolute image URL from a stored value. Relative storage keys are
/// prefixed with the configured public base URL; values that are already absolute
/// URLs (externally hosted logos) are returned unchanged. Null/empty → null.
/// </summary>
public interface IImageUrlComposer
{
    string? Compose(string? keyOrUrl);

    /// <summary>
    /// Inverse of <see cref="Compose"/>: reduces a value back to the relative
    /// storage key when it is a URL under the configured public base URL
    /// (clients resend the composed URL they last read). Raw keys and external
    /// absolute URLs are returned unchanged. Null/empty → null.
    /// </summary>
    string? Decompose(string? keyOrUrl);
}
