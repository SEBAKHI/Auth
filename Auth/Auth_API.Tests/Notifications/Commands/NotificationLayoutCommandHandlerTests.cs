using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Notifications.CreateNotificationLayout;
using Auth.Application.Features.Notifications.PublishNotificationLayout;
using Auth.Application.Features.Notifications.UpdateNotificationLayoutDraft;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Commands;

/// <summary>
/// Handler tests for the layout publish gate: a layout must render cleanly AND
/// actually include the content slot — publishing one without it would blank
/// every subsequent message — AND, for email, write the card colour behind each
/// logo it shows, or the logo's built-in background frames it in a box.
/// </summary>
public class NotificationLayoutCommandHandlerTests
{
    private readonly Mock<INotificationLayoutRepository> _layoutRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<ITemplateCacheInvalidator> _cacheInvalidatorMock = new();
    private readonly Mock<IPlatformSettingsRepository> _platformRepoMock = new();
    private readonly Mock<IImageStorageService> _imageStorageMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    // A layout whose dark card has its own colour and that shows the dark logo on it: the case where
    // the logo's background is built in the default dark grey unless the layout says otherwise.
    private const string CustomDarkCardLayout =
        "<html><style>@media (prefers-color-scheme: dark) { .card { background-color:#3C6E47; } }</style>" +
        "<body>{% if Platform.EmailLogoDarkUrl %}<img src=\"{{ Platform.EmailLogoDarkUrl }}\">{% endif %}" +
        "{{ content | raw }}</body></html>";

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
            _platformRepoMock.Object,
            new FluidTemplateRenderer(TimeProvider.System),
            new Mock<IImageUrlComposer>().Object,
            _imageStorageMock.Object,
            TestHelpers.CreateOptions(new EmailSettings { SenderName = "Auth System" }),
            new Mock<ILogger<NotificationRenderingService>>().Object);

        return new PublishNotificationLayoutCommandHandler(
            _layoutRepoMock.Object,
            _appRepoMock.Object,
            renderer,
            _cacheInvalidatorMock.Object,
            new EmailLogoRenditions(
                _platformRepoMock.Object,
                _layoutRepoMock.Object,
                _imageStorageMock.Object,
                new Mock<ILogger<EmailLogoRenditions>>().Object),
            new Mock<ILogger<PublishNotificationLayoutCommandHandler>>().Object);
    }

    private NotificationLayout CreateLayout(string draftContent, CardColor? darkCard = null)
    {
        var layout = NotificationLayout.Create(
            null, NotificationChannelType.Email, "Default", draftContent, "{}", null, darkCard, _userId).Value;
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
    public async Task Publish_CardRecolouredButCardColourLeftAtTheDefault_IsBlocked()
    {
        // The layout's dark card took another colour, nothing told the server, and the dark
        // logo was built on the default grey - a visible box in every message.
        var layout = CreateLayout(CustomDarkCardLayout);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, layout.ModifiedAt ?? layout.CreatedAt)
            { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutCardColorNotInLayout");
        result.FirstError.Description.Should().Contain("#1A1A1C");
        layout.IsPublished.Should().BeFalse();
        _layoutRepoMock.Verify(
            r => r.TryPublishAsync(It.IsAny<NotificationLayout>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Publish_CardColourMatchingTheCard_PublishesItAndBuildsItsBackgroundFirst()
    {
        var customCard = CardColor.Create("#3C6E47").Value;
        var layout = CreateLayout(CustomDarkCardLayout, darkCard: customCard);
        var expectedRevisionAt = layout.ModifiedAt ?? layout.CreatedAt;
        var settings = PlatformSettings.CreateDefault();
        settings.Update("Platform", "light.webp", "dark.webp", null, _userId);
        _platformRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        var order = new List<string>();
        _imageStorageMock
            .Setup(s => s.EnsureEmailLogoRenditionAsync(
                It.IsAny<string?>(), It.IsAny<EmailLogoVariant>(), It.IsAny<CardColor>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("build"))
            .ReturnsAsync((EmailLogoRendition?)null);
        _layoutRepoMock
            .Setup(r => r.TryPublishAsync(layout, expectedRevisionAt, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish"))
            .ReturnsAsync(true);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, expectedRevisionAt) { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        layout.PublishedCardColors.Dark.Should().Be(customCard);
        _imageStorageMock.Verify(
            s => s.EnsureEmailLogoRenditionAsync("dark.webp", EmailLogoVariant.Dark, customCard, It.IsAny<CancellationToken>()),
            Times.Once);
        order.Should().StartWith("build").And.EndWith("publish",
            "the first message sent on the new colour must find its background");
    }

    [Fact]
    public async Task Publish_CardPaintedFromTheLayoutVariable_Passes()
    {
        // The layout can paint its card from the very field the background is built from;
        // the check reads the rendered layout, so a colour written through it counts.
        var customCard = CardColor.Create("#3C6E47").Value;
        var layout = CreateLayout(
            "<html><style>.card { background-color:{{ Layout.CardColorDark }}; }</style>" +
            "<body><img src=\"{{ Platform.EmailLogoDarkUrl }}\">{{ content | raw }}</body></html>",
            darkCard: customCard);
        var expectedRevisionAt = layout.ModifiedAt ?? layout.CreatedAt;
        _layoutRepoMock
            .Setup(r => r.TryPublishAsync(layout, expectedRevisionAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new PublishNotificationLayoutCommand(layout.Id, expectedRevisionAt) { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    private UpdateNotificationLayoutDraftCommandHandler CreateDraftHandler() =>
        new(
            _layoutRepoMock.Object,
            _appRepoMock.Object,
            new FluidTemplateRenderer(TimeProvider.System),
            new EmailLogoRenditions(
                _platformRepoMock.Object,
                _layoutRepoMock.Object,
                _imageStorageMock.Object,
                new Mock<ILogger<EmailLogoRenditions>>().Object),
            new Mock<ILogger<UpdateNotificationLayoutDraftCommandHandler>>().Object);

    private static UpdateNotificationLayoutDraftCommand DraftSave(
        NotificationLayout layout, string? light, string? dark) =>
        new(layout.Id, layout.Name, layout.DraftContent, layout.DraftStringsJson, null, light, dark);

    private static readonly string NestedTooDeeply =
        string.Concat(Enumerable.Repeat("{% if x %}", 6_000)) + "{{ content | raw }}"
        + string.Concat(Enumerable.Repeat("{% endif %}", 6_000));

    [Fact]
    public async Task SaveDraft_NestedTooDeeplyToRender_IsRefused_AndNothingIsSaved()
    {
        var layout = CreateLayout("<html>{{ content | raw }}</html>");

        var result = await CreateDraftHandler().Handle(
            new UpdateNotificationLayoutDraftCommand(layout.Id, layout.Name, NestedTooDeeply, "{}", null, null, null),
            CancellationToken.None);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        _layoutRepoMock.Verify(r => r.UpdateAsync(It.IsAny<NotificationLayout>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NestedTooDeeplyToRender_IsRefused_AndNothingIsCreated()
    {
        var handler = new CreateNotificationLayoutCommandHandler(
            _layoutRepoMock.Object,
            _appRepoMock.Object,
            new FluidTemplateRenderer(TimeProvider.System),
            new EmailLogoRenditions(
                _platformRepoMock.Object,
                _layoutRepoMock.Object,
                _imageStorageMock.Object,
                new Mock<ILogger<EmailLogoRenditions>>().Object),
            new Mock<ILogger<CreateNotificationLayoutCommandHandler>>().Object);

        var result = await handler.Handle(
            new CreateNotificationLayoutCommand(null, NotificationChannelType.Email, "Deep", NestedTooDeeply, "{}")
            { CreatedBy = _userId },
            CancellationToken.None);

        result.FirstError.Should().Be(TemplateErrors.TooComplex);
        _layoutRepoMock.Verify(r => r.CreateAsync(It.IsAny<NotificationLayout>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Publish_LayoutThatRendersPastTheEmailCap_IsBlocked()
    {
        var layout = CreateLayout("<html>{% for i in (1..3000) %}0123456789012345678901234567890123456789{% endfor %}{{ content | raw }}</html>");

        var result = await CreateHandler().Handle(
            new PublishNotificationLayoutCommand(layout.Id, layout.ModifiedAt ?? layout.CreatedAt) { PublishedBy = _userId },
            CancellationToken.None);

        result.FirstError.Code.Should().Be(TemplateErrors.OutputTooLargeCode);
    }

    [Fact]
    public async Task SaveDraft_WithoutTheColourFields_KeepsTheSavedColours()
    {
        // A script, or a console tab opened before the colours existed, sends no colours.
        // That must not erase the ones an admin set.
        var customCard = CardColor.Create("#3C6E47").Value;
        var layout = CreateLayout("<html>{{ content | raw }}</html>", darkCard: customCard);

        var result = await CreateDraftHandler().Handle(DraftSave(layout, null, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        layout.DraftCardColorDark.Should().Be(customCard);
        result.Value.DraftCardColorDark.Should().Be("#3C6E47");
    }

    [Fact]
    public async Task SaveDraft_WithAnEmptyColour_ReturnsItToTheDefault()
    {
        var layout = CreateLayout("<html>{{ content | raw }}</html>", darkCard: CardColor.Create("#3C6E47").Value);

        var result = await CreateDraftHandler().Handle(DraftSave(layout, "#ABCDEF", ""), CancellationToken.None);

        result.IsError.Should().BeFalse();
        layout.DraftCardColorDark.Should().BeNull("an empty colour means the default");
        layout.DraftCardColorLight.Should().Be(CardColor.Create("#ABCDEF").Value);
    }

    // The shipped layout itself (seed 11), so the gate is held to the document most
    // deployments publish, not only to a synthetic one.
    private static string ShippedLayout() => EmailLayoutContractTests.SeededLayoutDocument();

    private async Task<ErrorOr.ErrorOr<Auth.Application.DTOs.NotificationLayoutDto>> PublishAsync(NotificationLayout layout)
    {
        var expectedRevisionAt = layout.ModifiedAt ?? layout.CreatedAt;
        _layoutRepoMock
            .Setup(r => r.TryPublishAsync(layout, expectedRevisionAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return await CreateHandler().Handle(
            new PublishNotificationLayoutCommand(layout.Id, expectedRevisionAt) { PublishedBy = _userId },
            CancellationToken.None);
    }

    [Fact]
    public async Task Publish_TheShippedLayoutAsShipped_Passes()
    {
        var result = await PublishAsync(CreateLayout(ShippedLayout()));

        result.IsError.Should().BeFalse("the shipped layout paints its cards in the default colours");
    }

    [Fact]
    public async Task Publish_TheShippedLayoutWithADarkCardColourItsCssNeverPaints_IsRefused()
    {
        // The colour is set, the CSS still paints the default dark grey: the logo would sit on
        // its own colour inside a grey card. Nothing in the layout may count as painting it,
        // a comment that prints the colour variable included.
        var customCard = CardColor.Create("#3C6E47").Value;

        var result = await PublishAsync(CreateLayout(ShippedLayout(), darkCard: customCard));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutCardColorNotInLayout");
        result.FirstError.Description.Should().Contain("#3C6E47");
    }

    [Fact]
    public async Task Publish_TheShippedLayoutRepaintedDarkWithTheColourLeftEmpty_IsRefused()
    {
        // The other way round: the dark palette repainted in the CSS, the colour field left at
        // the default, so the logo would be built on a grey that is no longer anywhere.
        var repainted = ShippedLayout().Replace("#1A1A1C", "#3C6E47", StringComparison.OrdinalIgnoreCase);

        var result = await PublishAsync(CreateLayout(repainted));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.LayoutCardColorNotInLayout");
        result.FirstError.Description.Should().Contain("#1A1A1C");
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
