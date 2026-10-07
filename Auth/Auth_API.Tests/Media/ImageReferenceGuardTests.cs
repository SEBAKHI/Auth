using Auth.Application.Common;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.ApplicationManagement.Commands;

namespace Auth_API.Tests.Media;

/// <summary>
/// Unit tests for <see cref="ImageReferenceGuard"/>: the rule every logo writer
/// applies before it stores a value. The composer is the real one, so composed
/// URLs are reduced to keys exactly as in production.
/// </summary>
public class ImageReferenceGuardTests
{
    private const string Base = ApplicationTestImages.PublicBaseUrl;
    private const string OwnKey = "a1b2c3d4e5f60718293a4b5c6d7e8f90.webp";

    private readonly Guid _actor = Guid.NewGuid();

    /// <summary>A ledger that fails the test on any call.</summary>
    private static Mock<IUploadedImageRepository> NoLedgerCalls() => new(MockBehavior.Strict);

    private static ImageReferenceGuard Guard(Mock<IUploadedImageRepository> uploadedImages) =>
        new(uploadedImages.Object, ApplicationTestImages.Composer());

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "stored.webp")]
    [InlineData("   ", "stored.webp")]
    [InlineData("stored.webp", "stored.webp")]
    [InlineData(Base + "/stored.webp", "stored.webp")]
    [InlineData(Base + "/stored.webp", Base + "/stored.webp")]
    [InlineData("http://legacy.example.com/logo.png", "http://legacy.example.com/logo.png")]
    [InlineData("https://cdn.other.example/logo.svg", null)]
    [InlineData("HTTPS://cdn.other.example/logo.svg", "stored.webp")]
    public async Task EnsureCanStore_ClearedUnchangedOrExternalHttps_PassesWithoutTheLedger(
        string? incoming, string? stored)
    {
        var uploadedImages = NoLedgerCalls();

        var result = await Guard(uploadedImages).EnsureCanStoreAsync(
            incoming, stored, _actor, CancellationToken.None);

        result.IsError.Should().BeFalse();
        uploadedImages.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("http://cdn.other.example/logo.png")]
    [InlineData("someone-elses.webp")]
    [InlineData("never-uploaded.webp")]
    [InlineData(Base + "/someone-elses.webp")]
    public async Task EnsureCanStore_AnythingTheActorCannotClaim_ReturnsImageNotAvailable(string incoming)
    {
        // The ledger grants nothing to this actor: a key somebody else uploaded
        // and a key nobody uploaded both affect zero rows. An http:// URL is not
        // an external https logo, so it lands here as a key and is refused.
        var uploadedImages = new Mock<IUploadedImageRepository>();
        uploadedImages
            .Setup(r => r.TryClaimAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Guard(uploadedImages).EnsureCanStoreAsync(
            incoming, "stored.webp", _actor, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ImageErrors.NotAvailable.Code);
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Validation);
        uploadedImages.Verify(
            r => r.TryClaimAsync(It.IsAny<string>(), _actor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(OwnKey)]
    [InlineData(Base + "/" + OwnKey)]
    public async Task EnsureCanStore_TheActorsOwnUpload_IsClaimedAsTheKeyWithThatActor(string incoming)
    {
        var uploadedImages = new Mock<IUploadedImageRepository>();
        uploadedImages
            .Setup(r => r.TryClaimAsync(OwnKey, _actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Guard(uploadedImages).EnsureCanStoreAsync(
            incoming, stored: null, _actor, CancellationToken.None);

        result.IsError.Should().BeFalse();
        uploadedImages.Verify(
            r => r.TryClaimAsync(OwnKey, _actor, It.IsAny<CancellationToken>()), Times.Once);
        uploadedImages.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnsureCanStore_PassesTheCallersToken()
    {
        using var source = new CancellationTokenSource();
        var uploadedImages = new Mock<IUploadedImageRepository>();
        uploadedImages
            .Setup(r => r.TryClaimAsync(OwnKey, _actor, source.Token))
            .ReturnsAsync(true);

        var result = await Guard(uploadedImages).EnsureCanStoreAsync(
            OwnKey, stored: null, _actor, source.Token);

        result.IsError.Should().BeFalse();
        uploadedImages.Verify(r => r.TryClaimAsync(OwnKey, _actor, source.Token), Times.Once);
    }
}
