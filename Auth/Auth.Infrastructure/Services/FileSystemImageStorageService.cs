using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using ErrorOr;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace Auth.Infrastructure.Services;

/// <summary>
/// Filesystem-backed <see cref="IImageStorageService"/>. Decoding with SkiaSharp validates
/// that the upload is a genuine raster image (rejecting polyglots, SVG and HTML); the image is
/// resized down to the configured max edge and re-encoded to WebP, which drops all metadata
/// (EXIF/GPS). Files are written under a random key. Swap for an Azure Blob / S3 implementation
/// later without touching callers.
/// </summary>
public sealed class FileSystemImageStorageService : IImageStorageService
{
    private readonly ImageStorageSettings _settings;

    public FileSystemImageStorageService(IOptions<ImageStorageSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task<ErrorOr<string>> SaveImageAsync(
        Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (!_settings.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation("Image.UnsupportedType", $"Unsupported image type '{contentType}'.");
        }

        // Buffer to memory so the (possibly non-seekable) request stream can be decoded.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var original = SKBitmap.Decode(buffer);
        if (original is null)
        {
            return Error.Validation("Image.Invalid", "The uploaded file is not a valid image.");
        }

        var max = _settings.MaxEdgePx;
        SKBitmap? scaled = null;
        try
        {
            var source = original;
            if (max > 0 && (original.Width > max || original.Height > max))
            {
                var ratio = Math.Min((float)max / original.Width, (float)max / original.Height);
                var width = Math.Max(1, (int)(original.Width * ratio));
                var height = Math.Max(1, (int)(original.Height * ratio));
                scaled = original.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
                if (scaled is not null)
                {
                    source = scaled;
                }
            }

            using var image = SKImage.FromBitmap(source);
            // Re-encoding produces a clean WebP with no source metadata.
            using var data = image.Encode(SKEncodedImageFormat.Webp, _settings.WebpQuality);
            if (data is null)
            {
                return Error.Validation("Image.Invalid", "The image could not be processed.");
            }

            var root = ResolvedRoot();
            Directory.CreateDirectory(root);
            var key = $"{Guid.NewGuid():N}.webp";
            var fullPath = Path.Combine(root, key);

            await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                data.SaveTo(fs);
            }

            return key;
        }
        finally
        {
            scaled?.Dispose();
        }
    }

    public Task DeleteImageAsync(string? key, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(key) && !IsAbsoluteUrl(key))
        {
            // Path.GetFileName collapses any directory traversal in a stored key.
            var safeName = Path.GetFileName(key);
            var path = Path.Combine(ResolvedRoot(), safeName);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup — a failed delete must not fail the operation.
            }
        }

        return Task.CompletedTask;
    }

    private string ResolvedRoot() =>
        Path.IsPathRooted(_settings.PhysicalPath)
            ? _settings.PhysicalPath
            : Path.Combine(AppContext.BaseDirectory, _settings.PhysicalPath);

    private static bool IsAbsoluteUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
