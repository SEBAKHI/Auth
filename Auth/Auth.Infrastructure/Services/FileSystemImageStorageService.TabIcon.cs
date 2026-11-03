using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Auth.Infrastructure.Services;

// The tab icon: one PNG at a stable URL that both applications' HTML names.
//
// The applications are static files, so their HTML cannot know which icon an administrator
// uploaded. It used to name the Vite template's icon, and a script swapped the platform's in
// once the page had loaded its branding: every load showed Vite first, and Safari, which does
// not reliably follow an icon swapped in by a script, kept showing it. A stable URL is what a
// static page can name, so the icon is right from the browser's first parse of the page.
//
// PNG, because a browser's tab icon decoder is not its page decoder: a format every browser
// shows as a tab icon, which the stored WebP is not known to be. Square and transparent, the image
// shown whole in the middle, like the frame editor's favicon mode.
public sealed partial class FileSystemImageStorageService
{
    /// <summary>
    /// The tab icon's STABLE key. Named in both applications' HTML
    /// (`Auth_UI/packages/api/src/platform-favicon.ts`): renaming it breaks the icon of every deployed page until it is rebuilt.
    /// </summary>
    public const string TabIconKey = "platform-favicon.png";

    /// <summary>
    /// The edge in pixels: a 16px tab on a 3x screen draws 48, a bookmark or a start page more.
    /// The browser scales it down for the rest.
    /// </summary>
    internal const int TabIconEdgePx = 128;

    // The default mark is the sidebar's: the ShieldCheck icon (lucide) at 20px on a 32px tile
    // with the preset's radius (0.625rem), in the preset's primary and primary-foreground
    // (oklch 0.205 0 0 and oklch 0.985 0 0). A file, not the page: it has no theme to read.
    private const float DefaultTileRadiusPx = TabIconEdgePx * 10f / 32f;
    private const float DefaultIconScale = TabIconEdgePx * 20f / 32f / 24f;
    private static readonly SKColor DefaultTileColour = new(0x17, 0x17, 0x17);
    private static readonly SKColor DefaultMarkColour = new(0xFA, 0xFA, 0xFA);

    private static readonly string[] ShieldCheckPaths =
    [
        "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
        "m9 12 2 2 4-4",
    ];

    /// <inheritdoc />
    public Task<bool> EnsureTabIconRenditionAsync(string? sourceKey, CancellationToken cancellationToken)
    {
        var root = ResolvedRoot(_settings.CurrentValue);
        var path = Path.Combine(root, TabIconKey);

        using var surface = SKSurface.Create(
            new SKImageInfo(TabIconEdgePx, TabIconEdgePx, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface is null)
        {
            _logger.LogWarning("Tab icon skipped: no raster surface for {Edge}px.", TabIconEdgePx);
            return Task.FromResult(false);
        }

        surface.Canvas.Clear(SKColors.Transparent);
        if (!DrawSource(surface.Canvas, sourceKey, root))
        {
            DrawDefaultMark(surface.Canvas);
        }
        surface.Canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            _logger.LogWarning("Tab icon skipped: PNG encode failed.");
            return Task.FromResult(false);
        }

        try
        {
            ReplaceStableFile(root, path, data);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogError(ex,
                "Tab icon write failed for {Path}. Browsers keep the previous icon, or show none " +
                "if there is none. Verify the app-pool identity has write permission on " +
                "ImageStorage:PhysicalPath.", path);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// Draws the stored image whole and centred, scaled to the icon's edge. False when there
    /// is nothing to draw, so the caller draws the default mark instead.
    /// </summary>
    private bool DrawSource(SKCanvas canvas, string? sourceKey, string root)
    {
        // An externally hosted image is the administrator's own: there is no file to scale.
        if (string.IsNullOrWhiteSpace(sourceKey) || IsAbsoluteUrl(sourceKey))
        {
            return false;
        }

        // Path.GetFileName collapses any directory traversal in a stored key.
        var sourceName = Path.GetFileName(sourceKey);
        var sourcePath = Path.Combine(root, sourceName);
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning(
                "Tab icon falls back to the default mark: source image {Source} is missing from " +
                "{Root}. Re-upload the favicon in Platform settings.", sourceName, root);
            return false;
        }

        using var source = SKBitmap.Decode(sourcePath);
        if (source is null || source.Width <= 0 || source.Height <= 0)
        {
            _logger.LogWarning("Tab icon falls back to the default mark: {Source} could not be decoded.", sourceName);
            return false;
        }

        var ratio = Math.Min((float)TabIconEdgePx / source.Width, (float)TabIconEdgePx / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var height = Math.Max(1, (int)Math.Round(source.Height * ratio));

        using var scaled = source.Resize(
            new SKImageInfo(width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (scaled is null)
        {
            _logger.LogWarning("Tab icon falls back to the default mark: {Source} could not be resized.", sourceName);
            return false;
        }

        using var mark = SKImage.FromBitmap(scaled);
        // Already resampled by Resize, so this draw is 1:1.
        canvas.DrawImage(
            mark,
            new SKPoint((TabIconEdgePx - width) / 2f, (TabIconEdgePx - height) / 2f),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
        return true;
    }

    private static void DrawDefaultMark(SKCanvas canvas)
    {
        using var tile = new SKPaint { Color = DefaultTileColour, IsAntialias = true };
        canvas.DrawRoundRect(0, 0, TabIconEdgePx, TabIconEdgePx, DefaultTileRadiusPx, DefaultTileRadiusPx, tile);

        // The icon's 24-unit box, centred on the tile; its stroke is 2 units, as lucide draws it.
        var offset = (TabIconEdgePx - (24 * DefaultIconScale)) / 2;
        canvas.Save();
        canvas.Translate(offset, offset);
        canvas.Scale(DefaultIconScale);

        using var stroke = new SKPaint
        {
            Color = DefaultMarkColour,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        foreach (var data in ShieldCheckPaths)
        {
            using var path = SKPath.ParseSvgPathData(data);
            canvas.DrawPath(path, stroke);
        }

        canvas.Restore();
    }
}
