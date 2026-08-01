using Auth.Application.Configuration;
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

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
