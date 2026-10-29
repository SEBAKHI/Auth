using System.Security.Claims;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.Media.Controllers;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Media;

/// <summary>
/// <c>POST Images?purpose=Panel</c> keeps up to <c>ImageStorage:PanelMaxEdgePx</c> (2048) for an
/// image inserted into a sign-in page design. It is for the people who edit designs: holders of
/// <c>applications:update</c> or <c>platform-settings:manage</c> (owner, 2026-10-29). Everyone
/// else is refused with 403 and a published code, before the file or the quota is read; the
/// default purpose stays open to every signed-in person (their photo, their organization's logo).
/// </summary>
/// <remarks>
/// Both directions are asserted: only checking the granted case would pass a check hardwired to
/// true, and only checking the refusal would pass one hardwired to false.
/// </remarks>
public class ImagesControllerPurposeTests
{
    private static readonly Guid Uploader = Guid.NewGuid();

    [Fact]
    public async Task APanelImage_FromAPersonWhoEditsNoDesign_IsRefusedWith403_BeforeAnythingIsRead()
    {
        var images = new Images(permission: null);

        var result = await images.Controller.Upload(images.File.Object, ImageUploadPurpose.Panel, CancellationToken.None);

        var refusal = result.Should().BeOfType<ObjectResult>().Subject;
        refusal.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var problem = refusal.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Extensions["code"].Should().Be("Auth.Unauthorized");
        images.File.Verify(file => file.OpenReadStream(), Times.Never);
        images.UploadedImages.Verify(
            repository => repository.GetUsedBytesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        images.Storage.Verify(
            storage => storage.SaveImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<ImageUploadPurpose>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    // Another permission a console administrator may hold does not do.
    [InlineData(null)]
    [InlineData(PermissionCodes.Applications.Read)]
    [InlineData(PermissionCodes.Users.Manage)]
    public async Task EveryPurposeButTheDefault_IsRefused_WithoutADesignPermission(string? permission)
    {
        // A purpose added later inherits the refusal until someone decides who it is for.
        var purposes = Enum.GetValues<ImageUploadPurpose>().Where(purpose => purpose != ImageUploadPurpose.Default).ToList();
        purposes.Should().NotBeEmpty();

        foreach (var purpose in purposes)
        {
            var images = new Images(permission);

            var result = await images.Controller.Upload(images.File.Object, purpose, CancellationToken.None);

            result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden, $"{purpose} keeps more pixels than the default");
        }
    }

    [Theory]
    [InlineData(PermissionCodes.Applications.Update)]
    [InlineData(PermissionCodes.PlatformSettings.Manage)]
    // The same wildcard rules as [RequirePermission].
    [InlineData("applications:*")]
    public async Task APanelImage_FromADesignEditor_IsStoredForThePanel(string permission)
    {
        var images = new Images(permission);

        var result = await images.Controller.Upload(images.File.Object, ImageUploadPurpose.Panel, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().Be(new UploadImageResponse("key.webp", "https://api.test/uploads/images/key.webp"));
        images.Storage.Verify(
            storage => storage.SaveImageAsync(
                It.IsAny<Stream>(), "image/png", ImageUploadPurpose.Panel, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PermissionCodes.Applications.Update)]
    public async Task ADefaultImage_IsStoredForEverySignedInPerson_AsBefore(string? permission)
    {
        var images = new Images(permission);

        var result = await images.Controller.Upload(images.File.Object, cancellationToken: CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        images.Storage.Verify(
            storage => storage.SaveImageAsync(
                It.IsAny<Stream>(), "image/png", ImageUploadPurpose.Default, It.IsAny<CancellationToken>()),
            Times.Once);
        images.UploadedImages.Verify(
            repository => repository.RecordAsync("key.webp", Uploader, 120, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>The controller with its dependencies answered, for a caller holding at most one permission.</summary>
    private sealed class Images
    {
        public Mock<IImageStorageService> Storage { get; } = new();
        public Mock<IUploadedImageRepository> UploadedImages { get; } = new();
        public Mock<IFormFile> File { get; } = new();
        public ImagesController Controller { get; }

        public Images(string? permission)
        {
            File.SetupGet(file => file.Length).Returns(100);
            File.SetupGet(file => file.ContentType).Returns("image/png");
            File.Setup(file => file.OpenReadStream()).Returns(() => new MemoryStream(new byte[100]));

            Storage.Setup(storage => storage.SaveImageAsync(
                    It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<ImageUploadPurpose>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ErrorOr<string>)"key.webp");
            Storage.Setup(storage => storage.GetStoredSizeAsync("key.webp", It.IsAny<CancellationToken>()))
                .ReturnsAsync(120L);
            UploadedImages.Setup(repository => repository.GetUsedBytesAsync(Uploader, It.IsAny<CancellationToken>()))
                .ReturnsAsync(0L);

            var composer = new Mock<IImageUrlComposer>();
            composer.Setup(c => c.Compose("key.webp")).Returns("https://api.test/uploads/images/key.webp");
            var settings = new Mock<IOptionsSnapshot<ImageStorageSettings>>();
            settings.SetupGet(s => s.Value).Returns(new ImageStorageSettings());

            List<Claim> claims = [new Claim("sub", Uploader.ToString())];
            if (permission is not null)
            {
                claims.Add(new Claim(JwtClaimNames.Permissions, permission));
            }

            Controller = new ImagesController(Storage.Object, composer.Object, UploadedImages.Object, settings.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                        // What a refusal resolves: the problem factory and the error contract.
                        RequestServices = ControllerServices.Build()
                    }
                }
            };
        }
    }
}
