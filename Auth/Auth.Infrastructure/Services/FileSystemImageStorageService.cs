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
/// <para>
/// WebP is right for the web surfaces and wrong for email, so logos additionally get a derived
/// opaque PNG rendition keyed off the source name — see
/// <see cref="EnsureEmailLogoRenditionAsync"/> and <see cref="EmailLogoRendition"/> for why.
/// </para>
/// </summary>
public sealed class FileSystemImageStorageService : IImageStorageService
{
    private readonly IOptionsMonitor<ImageStorageSettings> _settings;
    private readonly ILogger<FileSystemImageStorageService> _logger;

    public FileSystemImageStorageService(
        IOptionsMonitor<ImageStorageSettings> settings,
        ILogger<FileSystemImageStorageService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> SaveImageAsync(
        Stream content, string contentType, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        if (!settings.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return Error.Validation("Image.UnsupportedType", $"Unsupported image type '{contentType}'.");
        }

        // Buffer to memory so the (possibly non-seekable) request stream can be decoded.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        // Read the header ONLY and reject oversized dimensions BEFORE decoding
        // pixels: a small, highly compressed "decompression bomb" can otherwise
        // declare enormous dimensions and force SKBitmap.Decode to allocate
        // width*height*4 bytes (gigabytes), crashing the worker.
        using var imageData = SKData.CreateCopy(buffer.ToArray());
        using (var codec = SKCodec.Create(imageData))
        {
            if (codec is null)
            {
                return Error.Validation("Image.Invalid", "The uploaded file is not a valid image.");
            }

            var megapixels = (long)codec.Info.Width * codec.Info.Height;
            var limit = (long)settings.MaxMegapixels * 1_000_000;
            if (limit > 0 && megapixels > limit)
            {
                return Error.Validation(
                    "Image.DimensionsTooLarge",
                    $"Image dimensions exceed the maximum of {settings.MaxMegapixels} megapixels.");
            }
        }

        using var original = SKBitmap.Decode(imageData);
        if (original is null)
        {
            return Error.Validation("Image.Invalid", "The uploaded file is not a valid image.");
        }

        var max = settings.MaxEdgePx;
        SKBitmap? scaled = null;
        try
        {
            var source = original;
            if (max > 0 && (original.Width > max || original.Height > max))
            {
                var ratio = Math.Min((float)max / original.Width, (float)max / original.Height);
                var width = Math.Max(1, (int)(original.Width * ratio));
                var height = Math.Max(1, (int)(original.Height * ratio));
                // Mitchell cubic (B = C = 1/3) is Skia's own replacement for the retired
                // SKFilterQuality.High, so downscaling keeps the exact quality it had before.
                scaled = original.Resize(
                    new SKImageInfo(width, height),
                    new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (scaled is not null)
                {
                    source = scaled;
                }
            }

            using var image = SKImage.FromBitmap(source);
            // Re-encoding produces a clean WebP with no source metadata.
            using var data = image.Encode(SKEncodedImageFormat.Webp, settings.WebpQuality);
            if (data is null)
            {
                return Error.Validation("Image.Invalid", "The image could not be processed.");
            }

            var root = ResolvedRoot(settings);
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

    public Task<EmailLogoRendition?> EnsureEmailLogoRenditionAsync(
        string? sourceKey, EmailLogoVariant variant, CancellationToken cancellationToken)
        => Task.FromResult(BuildEmailLogoRendition(sourceKey, variant, write: true));

    public Task<EmailLogoRendition?> GetEmailLogoRenditionAsync(
        string? sourceKey, EmailLogoVariant variant, CancellationToken cancellationToken)
        => Task.FromResult(BuildEmailLogoRendition(sourceKey, variant, write: false));

    public Task DeleteImageAsync(string? key, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(key) && !IsAbsoluteUrl(key))
        {
            // Path.GetFileName collapses any directory traversal in a stored key.
            var safeName = Path.GetFileName(key);
            var root = ResolvedRoot(_settings.CurrentValue);

            // Only the uploaded source is removed. The email renditions are NOT touched:
            // they live at stable keys shared across every logo the platform has ever had,
            // and mail already delivered points at them. Deleting one here would turn the
            // logo in the recipient's inbox into a broken image, permanently. Renditions
            // built by an older version under a source-derived name are left alone for the
            // same reason - the messages that reference them are still out there.
            var path = Path.Combine(root, safeName);
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

    #region Email logo renditions

    // The rendition is generated at 2x and displayed at half size, so it stays sharp on
    // high-density phone screens where transactional mail is mostly read.
    private const int RenditionScale = 2;

    /// <summary>
    /// FIXED artboard. Every rendition ever produced has exactly these dimensions, whatever
    /// the source logo's aspect ratio; the mark is scaled to fit inside and centred.
    /// </summary>
    /// <remarks>
    /// This is a PERMANENT CONTRACT, not a tuning knob. Sent mail bakes width/height into its
    /// <c>&lt;img&gt;</c> (Outlook's Word engine ignores <c>height:auto</c>) and those messages
    /// can never be edited, so the numbers here must keep matching the file the stable URL
    /// serves. Changing them would stretch the logo in every email already delivered. If a
    /// different size is ever genuinely needed, it has to ship as a NEW stable filename so
    /// historical mail keeps resolving the old one.
    /// </remarks>
    private const int ArtboardWidthPx = 200 * RenditionScale;
    private const int ArtboardHeightPx = 72 * RenditionScale;
    private const int PlatePaddingPx = 12 * RenditionScale;

    /// <summary>The CSS size the layout must state. Constant, by the contract above.</summary>
    private const int CssWidth = ArtboardWidthPx / RenditionScale;
    private const int CssHeight = ArtboardHeightPx / RenditionScale;

    // Never blow a tiny source up into a blurry smear; a small mark simply stays small.
    private const float MaxUpscale = 2f;

    /// <summary>
    /// Plate colours. These MUST track the email layout's <c>.card</c> background in each
    /// mode (light <c>#FFFFFF</c>, dark <c>#1A1A1C</c>) so the chip disappears into the card
    /// instead of framing it. See <c>Auth_DB\dbo\Scripts\SeedData\11_NotificationLayouts.sql</c>.
    /// </summary>
    private static SKColor PlateColor(EmailLogoVariant variant) => variant switch
    {
        EmailLogoVariant.Dark => new SKColor(0x1A, 0x1A, 0x1C),
        _ => new SKColor(0xFF, 0xFF, 0xFF)
    };

    /// <summary>
    /// STABLE key — deliberately independent of the source file.
    /// </summary>
    /// <remarks>
    /// An email carries a URL, not the image: the recipient's client fetches it every time the
    /// message is opened, for as long as the message exists. A key derived from the uploaded
    /// file changed on every re-upload, so replacing the logo turned the logo in all previously
    /// delivered mail into a dead link. A stable key inverts that: the URL never changes, so a
    /// rebrand flows through to mail already sent and nothing accumulates or has to be kept
    /// alive forever. The cost is that propagation is not instant — Gmail's image proxy caches
    /// what it fetched — but a stale logo is a far smaller failure than a missing one.
    /// </remarks>
    private static string RenditionKey(EmailLogoVariant variant) =>
        $"platform-email-{variant.ToString().ToLowerInvariant()}.png";

    /// <summary>
    /// Shared read/write core. With <paramref name="write"/> false this only reports an
    /// existing rendition, so the send path can never touch the disk for writing.
    /// </summary>
    private EmailLogoRendition? BuildEmailLogoRendition(string? sourceKey, EmailLogoVariant variant, bool write)
    {
        // An externally hosted logo is an explicit escape hatch: we cannot re-plate a URL we
        // do not own, and the admin has taken responsibility for its email safety.
        if (string.IsNullOrWhiteSpace(sourceKey) || IsAbsoluteUrl(sourceKey))
        {
            return null;
        }

        var root = ResolvedRoot(_settings.CurrentValue);
        // Path.GetFileName collapses any directory traversal in a stored key.
        var sourceName = Path.GetFileName(sourceKey);
        var renditionKey = RenditionKey(variant);
        var renditionPath = Path.Combine(root, renditionKey);

        if (!write)
        {
            // Report the artboard constants rather than measuring the file: the two are
            // guaranteed equal, and a send must not depend on a disk read succeeding.
            return File.Exists(renditionPath)
                ? new EmailLogoRendition(renditionKey, CssWidth, CssHeight)
                : null;
        }

        var sourcePath = Path.Combine(root, sourceName);
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning(
                "Email logo rendition skipped: source image {Source} is missing from {Root}. " +
                "Re-upload the logo in Platform settings.", sourceName, root);
            return null;
        }

        using var source = SKBitmap.Decode(sourcePath);
        if (source is null || source.Width <= 0 || source.Height <= 0)
        {
            _logger.LogWarning("Email logo rendition skipped: {Source} could not be decoded.", sourceName);
            return null;
        }

        // Fit the mark inside the FIXED artboard, preserving its aspect ratio. The artboard
        // itself never changes size, so the width/height baked into already-sent mail stay
        // correct no matter what shape the next logo is.
        var innerMaxWidth = ArtboardWidthPx - (PlatePaddingPx * 2);
        var innerMaxHeight = ArtboardHeightPx - (PlatePaddingPx * 2);
        var ratio = Math.Min(
            Math.Min((float)innerMaxWidth / source.Width, (float)innerMaxHeight / source.Height),
            MaxUpscale);

        var innerWidth = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var innerHeight = Math.Max(1, (int)Math.Round(source.Height * ratio));
        // Centre it; the surrounding plate is the card colour, so the letterboxing is
        // invisible except in clients that force-invert the card but never the image.
        var offsetX = (ArtboardWidthPx - innerWidth) / 2;
        var offsetY = (ArtboardHeightPx - innerHeight) / 2;

        using var scaled = source.Resize(
            new SKImageInfo(innerWidth, innerHeight),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (scaled is null)
        {
            _logger.LogWarning("Email logo rendition skipped: {Source} could not be resized.", sourceName);
            return null;
        }

        // Opaque surface first — an alpha-free raster is the whole point. Premul is only a
        // fallback for raster configurations Skia declines to allocate; the plate covers every
        // pixel either way, so the encoded PNG is fully opaque in both cases.
        using var surface =
            SKSurface.Create(new SKImageInfo(ArtboardWidthPx, ArtboardHeightPx, SKColorType.Bgra8888, SKAlphaType.Opaque))
            ?? SKSurface.Create(new SKImageInfo(ArtboardWidthPx, ArtboardHeightPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            _logger.LogWarning("Email logo rendition skipped: no raster surface for {Width}x{Height}.",
                ArtboardWidthPx, ArtboardHeightPx);
            return null;
        }

        surface.Canvas.Clear(PlateColor(variant));
        using (var mark = SKImage.FromBitmap(scaled))
        {
            // The bitmap was already resampled by Resize above, so this draw is 1:1 and the
            // sampling mode is immaterial - but the parameterless overload is obsolete.
            surface.Canvas.DrawImage(
                mark,
                new SKPoint(offsetX, offsetY),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        surface.Canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            _logger.LogWarning("Email logo rendition skipped: PNG encode failed for {Source}.", sourceName);
            return null;
        }

        var tempPath = $"{renditionPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(root);

            // Write to a sibling temp file, then swap it in. The rendition sits at a STABLE
            // URL that recipients fetch at unpredictable times, so writing in place would let
            // a reader see a half-written PNG - a broken logo caused by the act of fixing it.
            // File.Move with overwrite is atomic within a volume.
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                data.SaveTo(fs);
            }

            File.Move(tempPath, renditionPath, overwrite: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogError(ex,
                "Email logo rendition write failed for {Path}. Emails keep serving the previous " +
                "rendition, or fall back to the text wordmark if there is none. Verify the " +
                "app-pool identity has write permission on ImageStorage:PhysicalPath.",
                renditionPath);

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup of the partial file; the failure above is what matters.
            }

            return null;
        }

        return new EmailLogoRendition(renditionKey, CssWidth, CssHeight);
    }

    #endregion

    private static string ResolvedRoot(ImageStorageSettings settings) =>
        Path.IsPathRooted(settings.PhysicalPath)
            ? settings.PhysicalPath
            : Path.Combine(AppContext.BaseDirectory, settings.PhysicalPath);

    private static bool IsAbsoluteUrl(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
