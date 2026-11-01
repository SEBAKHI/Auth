using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.Features.Notifications.DeleteNotificationLayout;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Notifications;
using Auth.Infrastructure.Persistence;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Commands;

/// <summary>
/// Deleting a notification layout (D2/D11 of the UI batch): an application's layout goes and its
/// emails fall back to the channel's global layout; the global layout itself is refused, by the
/// entity and again by the delete statement.
/// </summary>
/// <remarks>
/// Deliberate breaks: let <c>EnsureDeletable</c> pass a global layout; drop
/// <c>AND [ApplicationId] IS NOT NULL</c> from the statement; drop the cache eviction (the
/// fallback render keeps the deleted layout).
/// </remarks>
public class NotificationLayoutDeleteTests
{
    private const string GlobalLayout = "<html><body>GLOBAL {{ content | raw }}</body></html>";
    private const string AppLayout = "<html><body>APP {{ content | raw }}</body></html>";

    private static readonly Guid Admin = Guid.NewGuid();

    private readonly Mock<INotificationLayoutRepository> _layouts = new();
    private readonly Mock<ITemplateCacheInvalidator> _invalidator = new();
    private readonly Mock<IPublisher> _publisher = new();

    private static NotificationLayout Layout(Guid? applicationId) =>
        NotificationLayout.Create(
            applicationId, NotificationChannelType.Email, "Layout", AppLayout, "{}", null, null, Admin).Value;

    private DeleteNotificationLayoutCommandHandler Handler(ITemplateCacheInvalidator? invalidator = null) =>
        new(_layouts.Object, invalidator ?? _invalidator.Object, _publisher.Object, TimeProvider.System,
            new Mock<ILogger<DeleteNotificationLayoutCommandHandler>>().Object);

    private void Stored(NotificationLayout layout, bool globalPublished = true)
    {
        _layouts.Setup(r => r.GetByIdAsync(layout.Id, It.IsAny<CancellationToken>())).ReturnsAsync(layout);
        _layouts
            .Setup(r => r.GetPublishedAsync(layout.Channel, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(globalPublished
                ? new NotificationLayoutRenderSource(Guid.NewGuid(), null, GlobalLayout, "{}", CardColors.Default)
                : null);
    }

    [Fact]
    public void EnsureDeletable_RefusesTheGlobalLayout_AndAllowsAnApplicationsLayout()
    {
        Layout(null).EnsureDeletable().FirstError.Code.Should().Be("Notification.CannotDeleteGlobalLayout");
        Layout(Guid.NewGuid()).EnsureDeletable().IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_ApplicationLayout_DeletesEvictsTheScopeAndPublishes()
    {
        var applicationId = Guid.NewGuid();
        var layout = Layout(applicationId);
        Stored(layout);
        _layouts.Setup(r => r.DeleteApplicationLayoutAsync(layout.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(
            new DeleteNotificationLayoutCommand(layout.Id) { DeletedBy = Admin }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _invalidator.Verify(i => i.InvalidateLayout(NotificationChannelType.Email, applicationId), Times.Once);
        _publisher.Verify(p => p.Publish(
            It.Is<NotificationLayoutDeletedEvent>(e =>
                e.NotificationLayoutId == layout.Id && e.ApplicationId == applicationId &&
                e.Name == "Layout" && e.TriggeredBy == Admin && e.EventId != Guid.Empty),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Delete_GlobalLayout_IsRefused_AndNothingIsDeleted()
    {
        var layout = Layout(null);
        Stored(layout);

        var result = await Handler().Handle(
            new DeleteNotificationLayoutCommand(layout.Id) { DeletedBy = Admin }, CancellationToken.None);

        result.FirstError.Should().Be(NotificationErrors.CannotDeleteGlobalLayout);
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
        _layouts.Verify(r => r.DeleteApplicationLayoutAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_WhenTheChannelHasNoPublishedGlobalLayout_IsRefused_AndNothingIsDeleted()
    {
        // Only email has a seeded global layout: an application's SMS layout may be all there is.
        var layout = NotificationLayout.Create(
            Guid.NewGuid(), NotificationChannelType.Sms, "SMS", AppLayout, "{}", null, null, Admin).Value;
        Stored(layout, globalPublished: false);

        var result = await Handler().Handle(new DeleteNotificationLayoutCommand(layout.Id), CancellationToken.None);

        result.FirstError.Code.Should().Be("Notification.LayoutFallbackMissing");
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Conflict);
        _layouts.Verify(r => r.DeleteApplicationLayoutAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_UnknownOrAlreadyDeletedLayout_IsNotFound_WithoutEvent()
    {
        var missing = await Handler().Handle(new DeleteNotificationLayoutCommand(Guid.NewGuid()), CancellationToken.None);
        missing.FirstError.Code.Should().Be("Notification.LayoutNotFound");

        var layout = Layout(Guid.NewGuid());
        Stored(layout);
        _layouts.Setup(r => r.DeleteApplicationLayoutAsync(layout.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var raced = await Handler().Handle(new DeleteNotificationLayoutCommand(layout.Id), CancellationToken.None);

        raced.FirstError.Code.Should().Be("Notification.LayoutNotFound");
        _invalidator.Verify(i => i.InvalidateLayout(It.IsAny<NotificationChannelType>(), It.IsAny<Guid?>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Repository_DeleteStatement_CanNeverRemoveAGlobalLayout()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var id = Guid.NewGuid();

        var deleted = await new NotificationLayoutRepository(db).DeleteApplicationLayoutAsync(id, CancellationToken.None);

        deleted.Should().BeTrue();
        Regex.Replace(db.LastCommand!.CommandText, @"\s+", " ").Trim().Should().Be(
            "DELETE FROM [dbo].[NotificationLayouts] WHERE [Id] = @Id AND [ApplicationId] IS NOT NULL");
        db.LastCommand.Parameters["Id"].Should().Be(id);
    }

    /// <summary>
    /// D11's proof: an application that had its own layout renders with it, and after that layout is
    /// deleted its very next email renders with the channel's global layout, through the same
    /// send-path cache the API holds, without throwing.
    /// </summary>
    [Fact]
    public async Task AfterDelete_TheApplicationsNextEmail_RendersWithTheGlobalLayout()
    {
        var applicationId = Guid.NewGuid();
        var layout = Layout(applicationId);
        Stored(layout);
        _layouts.Setup(r => r.DeleteApplicationLayoutAsync(layout.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var appLayoutPublished = true;
        _layouts
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => appLayoutPublished
                ? new NotificationLayoutRenderSource(layout.Id, applicationId, AppLayout, "{}", CardColors.Default)
                : null);
        _layouts
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLayoutRenderSource(Guid.NewGuid(), null, GlobalLayout, "{}", CardColors.Default));

        var templates = new Mock<INotificationTemplateRepository>();
        templates
            .Setup(r => r.GetPublishedAsync(
                NotificationTypeCodes.PasswordReset, NotificationChannelType.Email, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationTemplateRenderSource(
                Guid.NewGuid(), Guid.NewGuid(), 1, null, "en",
                [new NotificationTranslationRenderSource("en", "Reset", "<p>body</p>", null)]));

        var cache = new TemplateCache(new MemoryCache(new MemoryCacheOptions()));
        var renderer = new NotificationRenderingService(
            cache,
            templates.Object,
            _layouts.Object,
            new Mock<IUserRepository>().Object,
            new Mock<IApplicationRepository>().Object,
            new Mock<IPlatformSettingsRepository>().Object,
            new FluidTemplateRenderer(TimeProvider.System),
            new Mock<IImageUrlComposer>().Object,
            new Mock<IImageStorageService>().Object,
            TestHelpers.CreateOptions(new EmailSettings { SenderName = "Auth System" }),
            new Mock<ILogger<NotificationRenderingService>>().Object);
        var request = new NotificationRequest
        {
            TypeCode = NotificationTypeCodes.PasswordReset,
            RecipientAddress = "user@example.com",
            ApplicationId = applicationId
        };

        var before = await renderer.RenderAsync(request, CancellationToken.None);
        before.Value.BodyHtml.Should().Contain("APP");

        // The row is gone, and the delete evicts the application's cached layout.
        appLayoutPublished = false;
        (await Handler(cache).Handle(new DeleteNotificationLayoutCommand(layout.Id), CancellationToken.None))
            .IsError.Should().BeFalse();

        var after = await renderer.RenderAsync(request, CancellationToken.None);
        after.IsError.Should().BeFalse();
        after.Value.BodyHtml.Should().Contain("GLOBAL").And.NotContain("APP");
    }

    [Fact]
    public async Task AuditHandler_WritesTheDeletion_AndNeverThrows()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var handler = new NotificationLayoutDeletedAuditEventHandler(
            audit.Object, new Mock<ILogger<NotificationLayoutDeletedAuditEventHandler>>().Object);
        var layoutId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        await handler.Handle(
            new NotificationLayoutDeletedEvent(
                Guid.NewGuid(), layoutId, applicationId, NotificationChannelType.Email, "Billing mail", Admin, DateTimeOffset.UtcNow),
            CancellationToken.None);

        written!.Action.Should().Be(AuditActions.NotificationLayoutDeleted);
        written.EntityId.Should().Be(layoutId);
        written.ApplicationId.Should().Be(applicationId);
        written.PerformedBy.Should().Be(Admin);
        written.OldValues.Should().Contain("Billing mail").And.Contain("Email");

        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var act = () => handler.Handle(
            new NotificationLayoutDeletedEvent(
                Guid.NewGuid(), layoutId, applicationId, NotificationChannelType.Email, "x", Admin, DateTimeOffset.UtcNow),
            CancellationToken.None);
        await act.Should().NotThrowAsync();
    }
}
