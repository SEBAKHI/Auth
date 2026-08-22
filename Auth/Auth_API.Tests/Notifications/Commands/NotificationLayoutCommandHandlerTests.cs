using Auth.Application.Configuration;
using Auth.Application.Features.Notifications.PublishNotificationLayout;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Commands;

/// <summary>
/// Handler tests for the layout publish gate: a layout must render cleanly AND
/// actually include the content slot — publishing one without it would blank
/// every subsequent message.
/// </summary>
public class NotificationLayoutCommandHandlerTests
{
    private readonly Mock<INotificationLayoutRepository> _layoutRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<ITemplateCacheInvalidator> _cacheInvalidatorMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private PublishNotificationLayoutCommandHandler CreateHandler()
    {
        // Real Fluid pipeline: the probe's outcome depends on actual composition,
        // so the renderer is not mocked.
        var renderer = new NotificationRenderingService(
            new TemplateCache(new MemoryCache(new MemoryCacheOptions())),
            new Mock<INotificationTemplateRepository>().Object,
            _layoutRepoMock.Object,
            new Mock<IUserRepository>().Object,
            _appRepoMock.Object,
            new Mock<IPlatformSettingsRepository>().Object,
            new FluidTemplateRenderer(),
            new Mock<IImageUrlComposer>().Object,
            new Mock<IImageStorageService>().Object,
            TestHelpers.CreateOptions(new EmailSettings { SenderName = "Auth System" }),
            new Mock<ILogger<NotificationRenderingService>>().Object);

        return new PublishNotificationLayoutCommandHandler(
            _layoutRepoMock.Object,
            _appRepoMock.Object,
            renderer,
            _cacheInvalidatorMock.Object,
            new Mock<ILogger<PublishNotificationLayoutCommandHandler>>().Object);
    }

    private NotificationLayout CreateLayout(string draftContent)
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default", draftContent, "{}", _userId).Value;
        _layoutRepoMock
            .Setup(r => r.GetByIdAsync(layout.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(layout);
        return layout;
    }

    [Fact]
    public async Task Publish_LayoutWithContentSlot_Succeeds()
    {
        var layout = CreateLayout("<html dir=\"{{ dir }}\"><body>{{ content | raw }}</body></html>");
        var expectedRevisionAt = layout.ModifiedAt ?? layout.CreatedAt;
        _layoutRepoMock
            .Setup(r => r.TryPublishAsync(layout, expectedRevisionAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, expectedRevisionAt)
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        layout.IsPublished.Should().BeTrue();
        _layoutRepoMock.Verify(
            r => r.TryPublishAsync(
                layout,
                expectedRevisionAt,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _cacheInvalidatorMock.Verify(
            c => c.InvalidateLayout(NotificationChannelType.Email, null), Times.Once);
    }

    [Fact]
    public async Task Publish_LayoutWithEncodedContentSlot_StillSucceeds()
    {
        // {{ content }} without | raw HTML-encodes the body; the alphanumeric
        // probe marker survives encoding, so this legitimate variant passes.
        var layout = CreateLayout("<html><body>{{ content }}</body></html>");
        var expectedRevisionAt = layout.ModifiedAt ?? layout.CreatedAt;
        _layoutRepoMock
            .Setup(r => r.TryPublishAsync(layout, expectedRevisionAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, expectedRevisionAt)
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_LayoutMissingContentSlot_IsBlocked()
    {
        var layout = CreateLayout(
            "<html><body><p>Chrome only — the author deleted the slot.</p></body></html>");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(
                layout.Id,
                layout.ModifiedAt ?? layout.CreatedAt)
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutContentSlotMissing");
        layout.IsPublished.Should().BeFalse();
        _layoutRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<NotificationLayout>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _cacheInvalidatorMock.Verify(
            c => c.InvalidateLayout(It.IsAny<NotificationChannelType>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task Publish_LayoutWithBrokenSyntax_IsBlockedBySyntaxGate()
    {
        var layout = CreateLayout("<html><body>{% if %} broken {{ content | raw }}</body></html>");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(
                layout.Id,
                layout.ModifiedAt ?? layout.CreatedAt)
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.InvalidTemplateSyntax");
        layout.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_StaleRevision_ReturnsConflictBeforeRenderingOrWriting()
    {
        var layout = CreateLayout("<html><body>{{ content | raw }}</body></html>");
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, layout.CreatedAt.AddMinutes(-1))
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutPublishTargetChanged");
        _layoutRepoMock.Verify(
            r => r.TryPublishAsync(
                It.IsAny<NotificationLayout>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Publish_DatabaseRevisionRace_ReturnsConflictWithoutCacheInvalidation()
    {
        var layout = CreateLayout("<html><body>{{ content | raw }}</body></html>");
        var expectedRevisionAt = layout.ModifiedAt ?? layout.CreatedAt;
        _layoutRepoMock
            .Setup(r => r.TryPublishAsync(layout, expectedRevisionAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, expectedRevisionAt)
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutPublishTargetChanged");
        _cacheInvalidatorMock.Verify(
            invalidator => invalidator.InvalidateLayout(
                It.IsAny<NotificationChannelType>(),
                It.IsAny<Guid?>()),
            Times.Never);
    }
}
