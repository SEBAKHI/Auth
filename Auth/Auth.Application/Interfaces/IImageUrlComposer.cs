namespace Auth.Application.Interfaces;

/// <summary>
/// Composes an absolute image URL from a stored value. Relative storage keys are
/// prefixed with the configured public base URL; values that are already absolute
/// URLs (externally hosted logos) are returned unchanged. Null/empty → null.
/// </summary>
public interface IImageUrlComposer
{
    string? Compose(string? keyOrUrl);
}
