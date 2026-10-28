using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Media;

/// <summary>
/// The rebuild after a logo change: every background in use or on disk gets the new logo,
/// and one that cannot be rebuilt does not stop the rest.
/// </summary>
public class EmailLogoRenditionsTests
{
    private static readonly CardColor CustomCard = CardColor.Create("#3C6E47").Value;

    [Fact]
    public async Task RebuildAll_OneBackgroundFails_TheOthersAreStillRebuilt()
    {
        var settings = PlatformSettings.CreateDefault();
        settings.Update("Platform", "light.webp", "dark.webp", null, Guid.NewGuid());

        var storage = new Mock<IImageStorageService>();
        storage
            .Setup(s => s.GetBuiltEmailLogoBackgroundsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new EmailLogoBackground(EmailLogoVariant.Dark, CustomCard)]);
        storage
            .Setup(s => s.EnsureEmailLogoRenditionAsync(
                It.IsAny<string?>(), It.IsAny<EmailLogoVariant>(), It.IsAny<CardColor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailLogoRendition("platform-email-x.png", 200, 72));
        // The default dark background is locked, say: it throws.
        storage
            .Setup(s => s.EnsureEmailLogoRenditionAsync(
                It.IsAny<string?>(), EmailLogoVariant.Dark, CardColor.DefaultDark, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("locked"));

        var layouts = new Mock<INotificationLayoutRepository>();
        layouts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var renditions = new EmailLogoRenditions(
            new Mock<IPlatformSettingsRepository>().Object,
            layouts.Object,
            storage.Object,
            new Mock<ILogger<EmailLogoRenditions>>().Object);

        var built = await renditions.RebuildAllAsync(settings, CancellationToken.None);

        built.Should().Be(2, "the light default and the custom dark background are rebuilt");
        storage.Verify(s => s.EnsureEmailLogoRenditionAsync(
            "dark.webp", EmailLogoVariant.Dark, CustomCard, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.EnsureEmailLogoRenditionAsync(
            "light.webp", EmailLogoVariant.Light, CardColor.DefaultLight, It.IsAny<CancellationToken>()), Times.Once);
    }
}
