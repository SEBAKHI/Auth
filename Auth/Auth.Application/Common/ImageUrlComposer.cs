using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Auth.Application.Common;

/// <summary>
/// Default <see cref="IImageUrlComposer"/> — composes absolute URLs from relative
/// storage keys using <see cref="ImageStorageSettings.PublicBaseUrl"/>, and passes
/// through values that are already absolute URLs.
/// </summary>
public sealed class ImageUrlComposer : IImageUrlComposer
{
    private readonly ImageStorageSettings _settings;

    public ImageUrlComposer(IOptions<ImageStorageSettings> settings)
    {
        _settings = settings.Value;
    }

    public string? Compose(string? keyOrUrl)
    {
        if (string.IsNullOrWhiteSpace(keyOrUrl))
        {
            return null;
        }

        if (keyOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            keyOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return keyOrUrl;
        }

        return $"{_settings.PublicBaseUrl.TrimEnd('/')}/{keyOrUrl.TrimStart('/')}";
    }

    public string? Decompose(string? keyOrUrl)
    {
        if (string.IsNullOrWhiteSpace(keyOrUrl))
        {
            return null;
        }

        var basePrefix = $"{_settings.PublicBaseUrl.TrimEnd('/')}/";
        if (keyOrUrl.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return keyOrUrl[basePrefix.Length..];
        }

        return keyOrUrl;
    }
}
