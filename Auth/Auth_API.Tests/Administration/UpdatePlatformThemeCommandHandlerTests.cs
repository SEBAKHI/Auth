using Auth.Application.Features.Platform.UpdatePlatformTheme;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Modules.AuditLog.EventHandlers;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Administration;

public class UpdatePlatformThemeCommandHandlerTests
{
    private readonly Mock<IPlatformSettingsRepository> _settingsRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IPublisher> _publisherMock = new();
    private readonly UpdatePlatformThemeCommandHandler _handler;

    public UpdatePlatformThemeCommandHandlerTests()
    {
        _userRepoMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User>());
        _handler = new UpdatePlatformThemeCommandHandler(
            _settingsRepoMock.Object,
            _userRepoMock.Object,
            Mock.Of<IImageUrlComposer>(),
            _publisherMock.Object,
            Mock.Of<ILogger<UpdatePlatformThemeCommandHandler>>());
    }

    private static UpdatePlatformThemeCommand Command(
        ThemeColorChoice? theme = null,
        string radius = "default",
        Guid? updatedBy = null) =>
        new(
            new ThemeColorChoice("stone"),
            theme ?? new ThemeColorChoice("amber"),
            new ThemeColorChoice("custom", "#16A34A", "#22c55e"),
            radius,
            "bold",
            updatedBy ?? Guid.NewGuid());

    [Fact]
    public async Task Handle_ValidAppearance_WritesTheAppearanceOnly()
    {
        var existing = new PlatformSettings(
            PlatformSettings.SingletonId, "YourBrand", "logo.webp", "logo-dark.webp", "favicon.webp", null, null);
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var actor = Guid.NewGuid();

        var result = await _handler.Handle(Command(updatedBy: actor), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Theme.Base.Preset.Should().Be("stone");
        result.Value.Theme.Theme.Preset.Should().Be("amber");
        result.Value.Theme.Chart.Light.Should().Be("#16a34a");
        result.Value.Theme.MenuAccent.Should().Be("bold");
        result.Value.ModifiedBy.Should().Be(actor);
        _settingsRepoMock.Verify(
            r => r.UpdateThemeAsync(
                It.Is<PlatformSettings>(s => s.Theme.Base.Preset == "stone" && s.ModifiedBy == actor),
                It.IsAny<CancellationToken>()),
            Times.Once());
        // The branding write would put back the logos this handler read: a
        // logo replaced in another tab meanwhile must survive a colour change.
        _settingsRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_ValidAppearance_PublishesOldAndNew()
    {
        _settingsRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((PlatformSettings?)null);

        await _handler.Handle(Command(), CancellationToken.None);

        _publisherMock.Verify(
            p => p.Publish(
                It.Is<PlatformThemeUpdatedEvent>(e =>
                    e.OldTheme == PlatformTheme.Default && e.NewTheme.Theme.Preset == "amber"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_InvalidAppearance_WritesAndPublishesNothing()
    {
        var result = await _handler.Handle(
            Command(theme: new ThemeColorChoice("zinc"), radius: "huge"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Errors.Select(e => e.Code).Should().BeEquivalentTo(
            ["SystemSettings.ThemeColorUnavailableForBase", "SystemSettings.ThemeUnknownRadius"]);
        _settingsRepoMock.Verify(
            r => r.UpdateThemeAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<PlatformThemeUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}

public class PlatformThemeUpdatedAuditEventHandlerTests
{
    [Fact]
    public async Task Handle_RecordsTheOldAndNewAppearance()
    {
        var repo = new Mock<IAuditLogRepository>();
        var handler = new PlatformThemeUpdatedAuditEventHandler(
            repo.Object, Mock.Of<ILogger<PlatformThemeUpdatedAuditEventHandler>>());
        var updatedBy = Guid.NewGuid();
        var newTheme = PlatformTheme.Create(
            new ThemeColorChoice("neutral"),
            new ThemeColorChoice("custom", "#112233", "#445566"),
            new ThemeColorChoice("cyan"),
            "default",
            "subtle").Value;

        await handler.Handle(
            new PlatformThemeUpdatedEvent(Guid.NewGuid(), PlatformTheme.Default, newTheme, updatedBy),
            CancellationToken.None);

        repo.Verify(
            r => r.CreateAsync(
                It.Is<AuditLog>(log =>
                    log.Action == "platform-settings.updated" &&
                    log.EntityType == "PlatformSettings" &&
                    log.PerformedBy == updatedBy &&
                    log.UserId == null &&
                    log.OldValues!.Contains("\"theme\":{\"preset\":\"neutral\"") &&
                    log.NewValues!.Contains("\"theme\":{\"preset\":\"custom\",\"light\":\"#112233\",\"dark\":\"#445566\"}")),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
