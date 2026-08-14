using System.Net;
using System.Net.Http.Headers;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Services;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Infrastructure;

/// <summary>
/// Tests for the provider profile-picture import. Every case here is a guard on an
/// outbound fetch that runs on the sign-in path: none of them may throw, and none of
/// them may reach storage with something that was not a bounded image.
/// </summary>
public class ExternalAvatarImporterTests
{
    private const string PictureUrl = "https://lh3.googleusercontent.com/a/picture";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public int CallCount { get; private set; }
        public Uri? RequestedUri { get; private set; }

        public StubHandler(HttpResponseMessage response)
            : this((_, _) => Task.FromResult(response)) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedUri = request.RequestUri;
            return _respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage ImageResponse(
        byte[] body,
        string contentType = "image/png",
        HttpStatusCode status = HttpStatusCode.OK,
        bool declareLength = true)
    {
        // A StreamContent with no Content-Length is how a chunked response arrives, and
        // it is the case the byte ceiling has to survive without a declared size.
        HttpContent content = declareLength
            ? new ByteArrayContent(body)
            : new StreamContent(new MemoryStream(body));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(status) { Content = content };
    }

    private static (ExternalAvatarImporter Importer, Mock<IImageStorageService> Storage) CreateImporter(
        StubHandler handler,
        bool enabled = true,
        int timeoutMs = 3000,
        int maxBytes = 1024)
    {
        var storage = new Mock<IImageStorageService>();
        storage
            .Setup(s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("stored-key.webp");

        var externalAuth = new ExternalAuthSettings
        {
            AvatarImport = new ExternalAvatarImportSettings
            {
                Enabled = enabled,
                TimeoutMs = timeoutMs,
                MaxBytes = maxBytes
            }
        };
        var imageSettings = new ImageStorageSettings
        {
            AllowedContentTypes = ImageStorageSettings.DefaultAllowedContentTypes
        };

        var importer = new ExternalAvatarImporter(
            new HttpClient(handler),
            storage.Object,
            new TestOptionsMonitor<ExternalAuthSettings>(externalAuth),
            new TestOptionsMonitor<ImageStorageSettings>(imageSettings),
            NullLogger<ExternalAvatarImporter>.Instance);

        return (importer, storage);
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [Fact]
    public async Task TryImportAsync_Disabled_ReturnsNullWithoutRequesting()
    {
        var handler = new StubHandler(ImageResponse([1, 2, 3]));
        var (importer, storage) = CreateImporter(handler, enabled: false);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
        handler.CallCount.Should().Be(0);
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryImportAsync_NoPictureUrl_ReturnsNullWithoutRequesting(string? url)
    {
        var handler = new StubHandler(ImageResponse([1, 2, 3]));
        var (importer, _) = CreateImporter(handler);

        var key = await importer.TryImportAsync(url, CancellationToken.None);

        key.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("http://lh3.googleusercontent.com/a/picture")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("/relative/path.png")]
    public async Task TryImportAsync_NotAbsoluteHttps_ReturnsNullWithoutRequesting(string url)
    {
        var handler = new StubHandler(ImageResponse([1, 2, 3]));
        var (importer, _) = CreateImporter(handler);

        var key = await importer.TryImportAsync(url, CancellationToken.None);

        key.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task TryImportAsync_Redirect_ReturnsNull()
    {
        // Redirects are not followed by the handler, so a 3xx is simply not a success.
        var handler = new StubHandler(ImageResponse([1, 2, 3], status: HttpStatusCode.Found));
        var (importer, storage) = CreateImporter(handler);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
        storage.Verify(
            s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task TryImportAsync_NonImageContentType_ReturnsNull()
    {
        var handler = new StubHandler(ImageResponse([1, 2, 3], contentType: "text/html"));
        var (importer, storage) = CreateImporter(handler);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
        storage.Verify(
            s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task TryImportAsync_DeclaredLengthOverLimit_ReturnsNull()
    {
        var handler = new StubHandler(ImageResponse(new byte[4096]));
        var (importer, storage) = CreateImporter(handler, maxBytes: 1024);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
        storage.Verify(
            s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task TryImportAsync_BodyOverLimitWithNoDeclaredLength_ReturnsNullWithoutStoring()
    {
        // The one that matters: Content-Length is a claim. With none sent at all, only
        // the running total during the read can stop an unbounded body.
        var handler = new StubHandler(ImageResponse(new byte[4096], declareLength: false));
        var (importer, storage) = CreateImporter(handler, maxBytes: 1024);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
        storage.Verify(
            s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task TryImportAsync_SlowProvider_ReturnsNullOnTimeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ImageResponse([1, 2, 3]);
        });
        var (importer, _) = CreateImporter(handler, timeoutMs: 500);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
    }

    [Fact]
    public async Task TryImportAsync_TransportFailure_ReturnsNull()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        var (importer, _) = CreateImporter(handler);

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
    }

    [Fact]
    public async Task TryImportAsync_StorageRejectsTheImage_ReturnsNull()
    {
        var handler = new StubHandler(ImageResponse([1, 2, 3]));
        var (importer, storage) = CreateImporter(handler);
        storage
            .Setup(s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Validation("Image.Invalid", "not an image"));

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().BeNull();
    }

    [Fact]
    public async Task TryImportAsync_ValidImage_ReturnsTheStorageKey()
    {
        var body = new byte[512];
        Random.Shared.NextBytes(body);
        var handler = new StubHandler(ImageResponse(body));
        var (importer, storage) = CreateImporter(handler, maxBytes: 1024);

        // Recorded during the call, not asserted afterwards: the buffer is disposed
        // once the import returns, so reading it in Verify would throw.
        long? seenLength = null;
        long? seenPosition = null;
        string? seenContentType = null;
        storage
            .Setup(s => s.SaveImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, CancellationToken>((stream, contentType, _) =>
            {
                seenLength = stream.Length;
                seenPosition = stream.Position;
                seenContentType = contentType;
            })
            .ReturnsAsync("stored-key.webp");

        var key = await importer.TryImportAsync(PictureUrl, CancellationToken.None);

        key.Should().Be("stored-key.webp");
        handler.RequestedUri.Should().Be(new Uri(PictureUrl));
        // Rewound and complete: storage decodes from the start of the whole body.
        seenLength.Should().Be(body.Length);
        seenPosition.Should().Be(0);
        seenContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task TryImportAsync_CallerCancels_PropagatesRatherThanSwallowing()
    {
        // A cancelled request is the caller giving up, not a failed import — it must not
        // be reported as "no avatar" and let the sign-in carry on doing work.
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(async (_, ct) =>
        {
            await cts.CancelAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return ImageResponse([1, 2, 3]);
        });
        var (importer, _) = CreateImporter(handler, timeoutMs: 30000);

        var act = async () => await importer.TryImportAsync(PictureUrl, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
