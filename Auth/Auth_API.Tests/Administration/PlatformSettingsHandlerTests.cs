using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Platform.GetPlatformBranding;
using Auth.Application.Features.Platform.GetPlatformSettings;
using Auth.Application.Features.Platform.UpdatePlatformSettings;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Administration;

public class GetPlatformBrandingQueryHandlerTests
{
    private readonly Mock<IPlatformSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IImageUrlComposer> _imageUrlComposerMock = new();
    private readonly GetPlatformBrandingQueryHandler _handler;

    public GetPlatformBrandingQueryHandlerTests()
    {
        _imageUrlComposerMock
            .Setup(c => c.Compose(It.IsAny<string?>()))
            .Returns<string?>(key => key is null ? null : $"/uploads/images/{key}");
        _handler = new GetPlatformBrandingQueryHandler(
            _settingsRepoMock.Object,
            _imageUrlComposerMock.Object);
    }

    [Fact]
    public async Task Handle_NoSettingsRow_ReturnsDefaults()
    {
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PlatformSettings?)null);

        var result = await _handler.Handle(new GetPlatformBrandingQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PlatformName.Should().Be(PlatformSettings.DefaultPlatformName);
        result.Value.LogoUrl.Should().BeNull();
        result.Value.LogoUrlDark.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SettingsRowExists_ReturnsStoredBrandingWithComposedLogos()
    {
        var settings = new PlatformSettings(PlatformSettings.SingletonId, "Sebakhi Console", "logo.webp", "logo-dark.webp", null, DateTime.UtcNow, Guid.NewGuid());
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var result = await _handler.Handle(new GetPlatformBrandingQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PlatformName.Should().Be("Sebakhi Console");
        result.Value.LogoUrl.Should().Be("/uploads/images/logo.webp");
        result.Value.LogoUrlDark.Should().Be("/uploads/images/logo-dark.webp");
    }

    [Fact]
    public async Task Handle_NoDarkLogo_ReturnsNullDarkLogo()
    {
        var settings = new PlatformSettings(PlatformSettings.SingletonId, "Sebakhi Console", "logo.webp", null, null, DateTime.UtcNow, Guid.NewGuid());
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var result = await _handler.Handle(new GetPlatformBrandingQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LogoUrl.Should().Be("/uploads/images/logo.webp");
        result.Value.LogoUrlDark.Should().BeNull();
    }
}

public class GetPlatformSettingsQueryHandlerTests
{
    private readonly Mock<IPlatformSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly GetPlatformSettingsQueryHandler _handler;

    public GetPlatformSettingsQueryHandlerTests()
    {
        _handler = new GetPlatformSettingsQueryHandler(
            _settingsRepoMock.Object,
            _userRepoMock.Object,
            Mock.Of<IImageUrlComposer>());
    }

    [Fact]
    public async Task Handle_ResolvesModifierName()
    {
        var modifiedBy = Guid.NewGuid();
        var settings = new PlatformSettings(PlatformSettings.SingletonId, "Sebakhi Console", null, null, null, DateTime.UtcNow, modifiedBy);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _userRepoMock.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { TestHelpers.CreateUser(id: modifiedBy, firstName: "Platform", lastName: "Admin") });

        var result = await _handler.Handle(new GetPlatformSettingsQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PlatformName.Should().Be("Sebakhi Console");
        result.Value.ModifiedBy.Should().Be(modifiedBy);
        result.Value.ModifiedByName.Should().Be("Platform Admin");
    }
}

public class UpdatePlatformSettingsCommandHandlerTests
{
    private const string PublicBaseUrl = "https://auth.example.com/uploads/images";

    private readonly Mock<IPlatformSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IImageStorageService> _imageStorageMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly UpdatePlatformSettingsCommandHandler _handler;

    public UpdatePlatformSettingsCommandHandlerTests()
    {
        // Real composer so key normalization (Decompose) is exercised.
        var composer = new ImageUrlComposer(Options.Create(
            new ImageStorageSettings { PublicBaseUrl = PublicBaseUrl }));

        _handler = new UpdatePlatformSettingsCommandHandler(
            _settingsRepoMock.Object,
            _userRepoMock.Object,
            composer,
            _imageStorageMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<UpdatePlatformSettingsCommandHandler>>().Object);
    }

    private void SetupExisting(string? logoUrl = null, string? logoUrlDark = null, string? faviconUrl = null)
    {
        var existing = new PlatformSettings(PlatformSettings.SingletonId, "Auth Console", logoUrl, logoUrlDark, faviconUrl, null, null);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    }

    [Fact]
    public async Task Handle_PersistsUpdateAndPublishesEvent()
    {
        var updatedBy = Guid.NewGuid();
        SetupExisting();

        var result = await _handler.Handle(
            new UpdatePlatformSettingsCommand("Sebakhi Console", "logo.webp", "logo-dark.webp", "favicon.webp", updatedBy),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PlatformName.Should().Be("Sebakhi Console");
        result.Value.ModifiedBy.Should().Be(updatedBy);

        _settingsRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<PlatformSettings>(s =>
                    s.PlatformName == "Sebakhi Console" &&
                    s.LogoUrl == "logo.webp" &&
                    s.LogoUrlDark == "logo-dark.webp" &&
                    s.FaviconUrl == "favicon.webp" &&
                    s.ModifiedBy == updatedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<PlatformSettingsUpdatedEvent>(e =>
                    e.OldPlatformName == "Auth Console" &&
                    e.NewPlatformName == "Sebakhi Console" &&
                    e.OldLogoUrl == null &&
                    e.NewLogoUrl == "logo.webp" &&
                    e.OldLogoUrlDark == null &&
                    e.NewLogoUrlDark == "logo-dark.webp" &&
                    e.OldFaviconUrl == null &&
                    e.NewFaviconUrl == "favicon.webp" &&
                    e.UpdatedBy == updatedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NoSettingsRow_StartsFromDefaultsAndUpserts()
    {
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PlatformSettings?)null);

        var result = await _handler.Handle(
            new UpdatePlatformSettingsCommand("Sebakhi Console", null, null, null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _settingsRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<PlatformSettings>(s => s.Id == PlatformSettings.SingletonId && s.PlatformName == "Sebakhi Console"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ReplacedFavicon_DeletesOldFile()
    {
        SetupExisting(faviconUrl: "old-favicon.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand("Auth Console", null, null, "new-favicon.webp", Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync("old-favicon.webp", It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ReplacedLogo_DeletesOldFile()
    {
        SetupExisting(logoUrl: "old.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand("Auth Console", "new.webp", null, null, Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync("old.webp", It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ClearedLogo_DeletesOldFile()
    {
        SetupExisting(logoUrl: "logo.webp", logoUrlDark: "logo-dark.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand("Auth Console", "logo.webp", null, null, Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync("logo-dark.webp", It.IsAny<CancellationToken>()),
            Times.Once());
        _imageStorageMock.Verify(
            s => s.DeleteImageAsync("logo.webp", It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_UnchangedLogoKeys_DeletesNothing()
    {
        SetupExisting(logoUrl: "logo.webp", logoUrlDark: "logo-dark.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand("Renamed Console", "logo.webp", "logo-dark.webp", null, Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_UnchangedLogoResentAsComposedUrl_DeletesNothingAndStoresRawKey()
    {
        // The SPA resends the composed absolute URL it read (not the raw key)
        // when saving unrelated fields; that must not delete the live file,
        // and the stored value must be normalized back to the raw key.
        SetupExisting(logoUrl: "logo.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand(
                "Renamed Console", $"{PublicBaseUrl}/logo.webp", null, null, Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _settingsRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<PlatformSettings>(s => s.LogoUrl == "logo.webp"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_LegacyComposedUrlRowReplaced_DeletesOldFileByKey()
    {
        // Rows written before key normalization hold the composed absolute URL;
        // replacing such a logo must still delete the old file (by its key).
        SetupExisting(logoUrl: $"{PublicBaseUrl}/old.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand("Auth Console", "new.webp", null, null, Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync("old.webp", It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ExternalLogoUrl_IsStoredUnchanged()
    {
        SetupExisting();

        await _handler.Handle(
            new UpdatePlatformSettingsCommand(
                "Auth Console", "https://cdn.example.org/brand/logo.png", null, null, Guid.NewGuid()),
            CancellationToken.None);

        _settingsRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<PlatformSettings>(s => s.LogoUrl == "https://cdn.example.org/brand/logo.png"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_OldLightKeyMovedToDarkSlot_DeletesNothing()
    {
        SetupExisting(logoUrl: "logo.webp");

        await _handler.Handle(
            new UpdatePlatformSettingsCommand("Auth Console", null, "logo.webp", null, Guid.NewGuid()),
            CancellationToken.None);

        _imageStorageMock.Verify(
            s => s.DeleteImageAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}

public class UpdatePlatformSettingsCommandValidatorTests
{
    private readonly UpdatePlatformSettingsCommandValidator _validator = new();

    [Fact]
    public void EmptyPlatformName_IsRejected()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand("", null, null, null, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void PlatformNameTooLong_IsRejected()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand(new string('x', 201), null, null, null, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void LogoUrlDarkTooLong_IsRejected()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand("Sebakhi Console", null, new string('x', 2049), null, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void FaviconUrlTooLong_IsRejected()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand("Sebakhi Console", null, null, new string('x', 2049), Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidRequest_Passes()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand("Sebakhi Console", "logo.webp", "logo-dark.webp", null, Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}


