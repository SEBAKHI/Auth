using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using ErrorOr;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<FileSystemImageStorageService> _logger;

    public FileSystemImageStorageService(
        IOptions<ImageStorageSettings> settings,
        ILogger<FileSystemImageStorageService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
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
            var key = $"{Guid.NewGuid():N}.webp";
            var fullPath = Path.Combine(root, key);

            try
            {
                Directory.CreateDirectory(root);
                await using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                data.SaveTo(fs);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Environment fault (ACL denial, disk full, ...), not a bad upload. Surface a
                // typed error so it reaches logs and clients as a server fault, never a 4xx.
                _logger.LogError(ex,
                    "Image storage write failed for path {Path}. Verify the process identity has " +
                    "write permission on ImageStorage:PhysicalPath ({Root}).", fullPath, root);
                return Error.Unexpected(
                    "Image.StorageUnavailable",
                    "Image storage is not writable on the server. Contact the administrator.");
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
