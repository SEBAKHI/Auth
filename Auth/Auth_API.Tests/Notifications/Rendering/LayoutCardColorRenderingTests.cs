using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Rendering;

/// <summary>
/// The send path picks each email logo by the card colour of the layout that wraps the
/// message, so two layouts on different cards each get a logo without a box.
/// </summary>
public class LayoutCardColorRenderingTests
{
    private static readonly Guid AppId = Guid.NewGuid();
    private static readonly CardColor CustomCard = CardColor.Create("#3C6E47").Value;

    private readonly Mock<INotificationTemplateRepository> _templateRepoMock = new();
    private readonly Mock<INotificationLayoutRepository> _layoutRepoMock = new();
    private readonly Mock<IImageStorageService> _imageStorageMock = new();
    private readonly NotificationRenderingService _service;

    public LayoutCardColorRenderingTests()
    {
        // The global layout keeps the default cards; the application's layout has a dark card of its own colour.
        const string layout =
            "<header><img src=\"{{ Platform.EmailLogoDarkUrl }}\"><i>{{ Layout.CardColorDark }}</i></header>{{ content | raw }}";
        _layoutRepoMock
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLayoutRenderSource(Guid.NewGuid(), null, layout, "{}", CardColors.Default));
        _layoutRepoMock
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, AppId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLayoutRenderSource(
                Guid.NewGuid(), AppId, layout, "{}", CardColors.Of(null, CustomCard)));

        _templateRepoMock
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplateRenderSource(
                Guid.NewGuid(), Guid.NewGuid(), 1, null, "en",
                [new NotificationTranslationRenderSource("en", "Reset", "<p>{{ Platform.EmailLogoDarkUrl }}</p>", null)]));

        var settings = PlatformSettings.CreateDefault();
        settings.Update("Platform", "light.webp", "dark.webp", null, Guid.NewGuid());
        var platformRepo = new Mock<IPlatformSettingsRepository>();
        platformRepo.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        _imageStorageMock
            .Setup(s => s.GetEmailLogoRenditionAsync("dark.webp", EmailLogoVariant.Dark, CardColor.DefaultDark, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailLogoRendition("platform-email-dark.png", 200, 72));
        _imageStorageMock
            .Setup(s => s.GetEmailLogoRenditionAsync("dark.webp", EmailLogoVariant.Dark, CustomCard, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailLogoRendition("platform-email-dark-3c6e47.png", 200, 72));

        var composer = new Mock<IImageUrlComposer>();
        composer.Setup(c => c.Compose(It.IsAny<string?>())).Returns((string? key) => key is null ? null : $"https://auth.example.com/{key}");

        _service = new NotificationRenderingService(
            new TemplateCache(new MemoryCache(new MemoryCacheOptions())),
            _templateRepoMock.Object,
            _layoutRepoMock.Object,
            new Mock<IUserRepository>().Object,
            new Mock<IApplicationRepository>().Object,
            platformRepo.Object,
            new FluidTemplateRenderer(TimeProvider.System),
            composer.Object,
            _imageStorageMock.Object,
            TestHelpers.CreateOptions(new EmailSettings { SenderName = "Auth System" }),
            new Mock<ILogger<NotificationRenderingService>>().Object);
    }

    private async Task<string> RenderFor(Guid? applicationId)
    {
        var result = await _service.RenderAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.PasswordReset,
                RecipientAddress = "user@example.com",
                ApplicationId = applicationId,
                LanguageCode = "en"
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        return result.Value.BodyHtml;
    }

    [Fact]
    public async Task ALayoutOnACustomCard_ShowsTheLogoBuiltOnThatCard()
    {
        var html = await RenderFor(AppId);

        var header = html[..html.IndexOf("</header>", StringComparison.Ordinal)];
        header.Should().Contain("platform-email-dark-3c6e47.png");
        header.Should().Contain("<i>#3C6E47</i>", "the layout can paint its card from the same field");
    }

    [Fact]
    public async Task ALayoutOnTheDefaultCards_KeepsTheOriginalFile()
    {
        var html = await RenderFor(null);

        html.Should().Contain("platform-email-dark.png");
        html.Should().NotContain("3c6e47");
    }

    [Fact]
    public async Task TheTemplateBody_HasNoCardOfItsOwnAndSeesTheDefaultBackgrounds()
    {
        var html = await RenderFor(AppId);

        var body = html[html.IndexOf("</header>", StringComparison.Ordinal)..];
        body.Should().Contain("platform-email-dark.png");
    }
}
