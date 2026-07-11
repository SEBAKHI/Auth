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
    }

    [Fact]
    public async Task Handle_SettingsRowExists_ReturnsStoredBrandingWithComposedLogo()
    {
        var settings = new PlatformSettings(PlatformSettings.SingletonId, "Sebakhi Console", "logo.webp", DateTime.UtcNow, Guid.NewGuid());
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var result = await _handler.Handle(new GetPlatformBrandingQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PlatformName.Should().Be("Sebakhi Console");
        result.Value.LogoUrl.Should().Be("/uploads/images/logo.webp");
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
        var settings = new PlatformSettings(PlatformSettings.SingletonId, "Sebakhi Console", null, DateTime.UtcNow, modifiedBy);
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
    private readonly Mock<IPlatformSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly UpdatePlatformSettingsCommandHandler _handler;

    public UpdatePlatformSettingsCommandHandlerTests()
    {
        _handler = new UpdatePlatformSettingsCommandHandler(
            _settingsRepoMock.Object,
            _userRepoMock.Object,
            Mock.Of<IImageUrlComposer>(),
            _publisherMock.Object,
            new Mock<ILogger<UpdatePlatformSettingsCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_PersistsUpdateAndPublishesEvent()
    {
        var updatedBy = Guid.NewGuid();
        var existing = new PlatformSettings(PlatformSettings.SingletonId, "Auth Console", null, null, null);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await _handler.Handle(
            new UpdatePlatformSettingsCommand("Sebakhi Console", "logo.webp", updatedBy),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PlatformName.Should().Be("Sebakhi Console");
        result.Value.ModifiedBy.Should().Be(updatedBy);

        _settingsRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<PlatformSettings>(s => s.PlatformName == "Sebakhi Console" && s.LogoUrl == "logo.webp" && s.ModifiedBy == updatedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<PlatformSettingsUpdatedEvent>(e =>
                    e.OldPlatformName == "Auth Console" &&
                    e.NewPlatformName == "Sebakhi Console" &&
                    e.NewLogoUrl == "logo.webp" &&
                    e.UpdatedBy == updatedBy),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NoSettingsRow_StartsFromDefaultsAndUpserts()
    {
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PlatformSettings?)null);

        var result = await _handler.Handle(
            new UpdatePlatformSettingsCommand("Sebakhi Console", null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _settingsRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<PlatformSettings>(s => s.Id == PlatformSettings.SingletonId && s.PlatformName == "Sebakhi Console"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }
}

public class UpdatePlatformSettingsCommandValidatorTests
{
    private readonly UpdatePlatformSettingsCommandValidator _validator = new();

    [Fact]
    public void EmptyPlatformName_IsRejected()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand("", null, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void PlatformNameTooLong_IsRejected()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand(new string('x', 201), null, Guid.NewGuid()));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidRequest_Passes()
    {
        var result = _validator.Validate(new UpdatePlatformSettingsCommand("Sebakhi Console", "logo.webp", Guid.NewGuid()));

        result.IsValid.Should().BeTrue();
    }
}
