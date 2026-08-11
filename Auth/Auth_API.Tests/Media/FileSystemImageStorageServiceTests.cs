using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Services;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Media;

/// <summary>
/// Unit tests for FileSystemImageStorageService, focused on the upload guards
/// (content type, decode-bomb dimension cap, genuine-image validation).
/// </summary>
public class FileSystemImageStorageServiceTests : IDisposable
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "img-tests-" + Guid.NewGuid().ToString("N"));

    private FileSystemImageStorageService CreateService(int maxMegapixels = 50)
    {
        var settings = new ImageStorageSettings
        {
            PhysicalPath = _tempRoot,
            MaxSizeBytes = 8 * 1024 * 1024,
            MaxMegapixels = maxMegapixels,
            MaxEdgePx = 512,
            WebpQuality = 80,
            AllowedContentTypes = ["image/png", "image/jpeg", "image/webp", "image/gif"]
        };

        return new FileSystemImageStorageService(
            TestHelpers.CreateOptions(settings),
            new Mock<ILogger<FileSystemImageStorageService>>().Object);
    }

    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task SaveImageAsync_ValidSmallImage_Succeeds()
    {
        var service = CreateService();
        using var stream = new MemoryStream(MakePng(64, 64));

        var result = await service.SaveImageAsync(stream, "image/png", CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().EndWith(".webp");
    }

    [Fact]
    public async Task SaveImageAsync_DimensionsOverMegapixelCap_RejectedBeforeDecode()
    {
        // 1500x1500 = 2.25 MP; cap of 1 MP must reject it (the decode-bomb guard).
        var service = CreateService(maxMegapixels: 1);
        using var stream = new MemoryStream(MakePng(1500, 1500));

        var result = await service.SaveImageAsync(stream, "image/png", CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Image.DimensionsTooLarge");
    }

    [Fact]
    public async Task SaveImageAsync_UnsupportedContentType_Rejected()
    {
        var service = CreateService();
        using var stream = new MemoryStream(MakePng(32, 32));

        var result = await service.SaveImageAsync(stream, "image/svg+xml", CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Image.UnsupportedType");
    }

    [Fact]
    public async Task SaveImageAsync_NonImageBytes_Rejected()
    {
        var service = CreateService();
        using var stream = new MemoryStream("<html>not an image</html>"u8.ToArray());

        var result = await service.SaveImageAsync(stream, "image/png", CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Image.Invalid");
    }

    #region Email logo renditions

    /// <summary>A mark with real transparency - the shape that caused the reported bug.</summary>
    private static byte[] MakeTransparentPng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
            // Ink over the middle band only, so the corners stay fully transparent.
            canvas.DrawRect(SKRect.Create(0, height * 0.25f, width, height * 0.5f), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<string> StoreAsync(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var result = await CreateService().SaveImageAsync(stream, "image/png", CancellationToken.None);
        result.IsError.Should().BeFalse();
        return result.Value;
    }

    [Fact]
    public async Task EnsureEmailLogoRendition_TransparentSource_ProducesAFullyOpaquePng()
    {
        // THE load-bearing assertion. Gmail transcodes WebP to JPEG, which has no alpha, and
        // flattens whatever transparency remains onto black - which is how a logo became a
        // black rounded rectangle. If a single pixel here is not opaque, that bug is back.
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(400, 100));

        var rendition = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        rendition.Should().NotBeNull();
        rendition!.Key.Should().EndWith(".png", "WebP is undecodable in Outlook for Windows");

        using var written = SKBitmap.Decode(Path.Combine(_tempRoot, rendition.Key));
        written.Should().NotBeNull();

        var transparentPixels = 0;
        for (var x = 0; x < written!.Width; x++)
        {
            for (var y = 0; y < written.Height; y++)
            {
                if (written.GetPixel(x, y).Alpha != 255)
                {
                    transparentPixels++;
                }
            }
        }

        transparentPixels.Should().Be(0, "the plate must be baked into the raster, not painted with CSS");
    }

    [Fact]
    public async Task EnsureEmailLogoRendition_PlatesEachVariantToMatchItsCard()
    {
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(400, 100));

        var light = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);
        var dark = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Dark, CancellationToken.None);

        light.Should().NotBeNull();
        dark.Should().NotBeNull();
        light!.Key.Should().NotBe(dark!.Key, "each variant is its own file");

        // The corner is padding, so it is pure plate. These must track .card in the layout.
        CornerOf(light.Key).Should().Be(new SKColor(0xFF, 0xFF, 0xFF));
        CornerOf(dark.Key).Should().Be(new SKColor(0x1A, 0x1A, 0x1C));
    }

    [Theory]
    [InlineData(1023, 201)]  // wide wordmark
    [InlineData(300, 300)]   // square
    [InlineData(120, 400)]   // portrait
    public async Task EnsureEmailLogoRendition_ArtboardIsIdenticalWhateverTheSourceShape(int w, int h)
    {
        // The artboard is a PERMANENT CONTRACT. Sent mail bakes width/height into its <img>
        // and can never be edited, so a logo of a different shape must not change the file's
        // dimensions - otherwise every previously delivered message stretches it. The mark is
        // letterboxed inside instead; the plate is the card colour, so that is invisible.
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(w, h));

        var rendition = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        rendition.Should().NotBeNull();
        rendition!.Width.Should().Be(200);
        rendition.Height.Should().Be(72);

        using var codec = SKCodec.Create(Path.Combine(_tempRoot, rendition.Key));
        codec.Info.Width.Should().Be(400);
        codec.Info.Height.Should().Be(144);
    }

    [Fact]
    public async Task EnsureEmailLogoRendition_KeyIsStableAcrossDifferentSources()
    {
        // The whole point: an email carries a URL the recipient fetches for years. If the key
        // moved with the upload, replacing the logo would kill the logo in all delivered mail.
        var service = CreateService();
        var first = await StoreAsync(MakeTransparentPng(400, 100));
        var second = await StoreAsync(MakeTransparentPng(300, 300));
        first.Should().NotBe(second, "each upload gets its own random key");

        var a = await service.EnsureEmailLogoRenditionAsync(first, EmailLogoVariant.Light, CancellationToken.None);
        var b = await service.EnsureEmailLogoRenditionAsync(second, EmailLogoVariant.Light, CancellationToken.None);

        a!.Key.Should().Be(b!.Key, "the rendition URL must survive a logo replacement");
        a.Key.Should().Be("platform-email-light.png");
    }

    [Fact]
    public async Task EnsureEmailLogoRendition_ReplacingTheLogoOverwritesTheSameFile()
    {
        var service = CreateService();
        var first = await StoreAsync(MakeTransparentPng(400, 100));
        var rendition = await service.EnsureEmailLogoRenditionAsync(
            first, EmailLogoVariant.Light, CancellationToken.None);
        var path = Path.Combine(_tempRoot, rendition!.Key);
        var before = await File.ReadAllBytesAsync(path);

        // A visibly different logo: solid ink over the whole canvas rather than a centre band.
        var second = await StoreAsync(MakePng(400, 100));
        await service.EnsureEmailLogoRenditionAsync(second, EmailLogoVariant.Light, CancellationToken.None);

        Directory.GetFiles(_tempRoot, "platform-email-*.png").Should().HaveCount(1,
            "a replacement must reuse the file, not mint a second one");
        (await File.ReadAllBytesAsync(path)).Should().NotEqual(before, "the new logo must be in it");
        Directory.GetFiles(_tempRoot, "*.tmp").Should().BeEmpty("the atomic swap must leave no temp file");
    }

    [Fact]
    public async Task EnsureEmailLogoRendition_ReportsTheCssSizeSoOutlookDoesNotStretchIt()
    {
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(400, 100));

        var rendition = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        rendition.Should().NotBeNull();
        rendition!.Width.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(200);
        rendition.Height.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(72);

        // Generated at 2x for high-density screens, reported at 1x for the width/height attributes.
        using var codec = SKCodec.Create(Path.Combine(_tempRoot, rendition.Key));
        codec.Info.Width.Should().Be(rendition.Width * 2);
        codec.Info.Height.Should().Be(rendition.Height * 2);
    }

    [Fact]
    public async Task GetEmailLogoRendition_BeforeItIsBuilt_ReturnsNullWithoutWriting()
    {
        // The send path calls the read-only lookup. With the outbox enabled that runs inside the
        // HTTP request that triggers the mail, and the uploads volume is not reliably writable
        // on shared hosting - so it must never generate anything.
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(400, 100));
        var before = Directory.GetFiles(_tempRoot).Length;

        var rendition = await service.GetEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        rendition.Should().BeNull();
        Directory.GetFiles(_tempRoot).Length.Should().Be(before, "the read path must not write");
    }

    [Fact]
    public async Task GetEmailLogoRendition_AfterItIsBuilt_ReturnsTheSameSize()
    {
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(400, 100));
        var built = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        var read = await service.GetEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        read.Should().BeEquivalentTo(built);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://cdn.example.com/logo.png")]
    public async Task EnsureEmailLogoRendition_NothingToPlate_ReturnsNull(string? sourceKey)
    {
        var rendition = await CreateService().EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);

        rendition.Should().BeNull("an externally hosted logo is the admin's own escape hatch");
    }

    [Fact]
    public async Task EnsureEmailLogoRendition_MissingSourceFile_ReturnsNullInsteadOfThrowing()
    {
        // Production has already been in this state: PlatformSettings held a key whose file was
        // gone, and the layout emitted an <img> pointing at a 404.
        var rendition = await CreateService().EnsureEmailLogoRenditionAsync(
            "deadbeefdeadbeefdeadbeefdeadbeef.webp",
            EmailLogoVariant.Light,
            CancellationToken.None);

        rendition.Should().BeNull();
    }

    [Fact]
    public async Task DeleteImage_RemovesTheSourceButNeverTheEmailRenditions()
    {
        // Deleting a replaced upload is right; deleting its rendition is not. The rendition
        // is at a stable URL that mail already sitting in recipients' inboxes points at, and
        // that mail cannot be edited - removing the file breaks it permanently.
        var service = CreateService();
        var sourceKey = await StoreAsync(MakeTransparentPng(400, 100));
        var light = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Light, CancellationToken.None);
        var dark = await service.EnsureEmailLogoRenditionAsync(
            sourceKey, EmailLogoVariant.Dark, CancellationToken.None);

        await service.DeleteImageAsync(sourceKey, CancellationToken.None);

        File.Exists(Path.Combine(_tempRoot, sourceKey)).Should().BeFalse(
            "the superseded upload itself is no longer referenced by anything");
        File.Exists(Path.Combine(_tempRoot, light!.Key)).Should().BeTrue(
            "delivered mail fetches this URL every time it is opened");
        File.Exists(Path.Combine(_tempRoot, dark!.Key)).Should().BeTrue();
    }

    private SKColor CornerOf(string renditionKey)
    {
        using var bitmap = SKBitmap.Decode(Path.Combine(_tempRoot, renditionKey));
        var pixel = bitmap.GetPixel(0, 0);
        return new SKColor(pixel.Red, pixel.Green, pixel.Blue);
    }

    #endregion

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
