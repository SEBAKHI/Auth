using Auth.Application.Configuration;
using Auth.Application.Features.Notifications.CreateNotificationTemplate;
using Auth.Application.Features.Notifications.DeleteNotificationTemplate;
using Auth.Application.Features.Notifications.PublishNotificationTemplate;
using Auth.Application.Features.Notifications.RollbackNotificationTemplate;
using Auth.Application.Features.Notifications.UnpublishNotificationTemplate;
using Auth.Application.Features.Notifications.UpdateNotificationTemplateDraft;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Notifications;
using Auth.Infrastructure.Notifications;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Commands;

/// <summary>
/// Handler tests for the template lifecycle: create duplicates, draft save with
/// syntax gate and concurrency, publish gate (unknown variables), the
/// system-global unpublish/delete protection, and rollback.
/// </summary>
public class NotificationTemplateCommandHandlerTests
{
    private readonly Mock<INotificationTemplateRepository> _templateRepoMock = new();
    private readonly Mock<INotificationTypeRepository> _typeRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<ITemplateCacheInvalidator> _cacheInvalidatorMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _typeId = Guid.NewGuid();

    private NotificationType CreateType(bool isSystem = true, string sampleDataJson = "{}") => new(
        _typeId, "password-reset", "Password Reset", null, isSystem,
        "[]", sampleDataJson, true, DateTime.UtcNow, _userId, null, null);

    private NotificationTemplate CreateAggregate(Guid? applicationId = null, bool published = true)
    {
        var template = NotificationTemplate.Create(
            _typeId, applicationId, NotificationChannelType.Email, "en", _userId).Value;
        template.UpsertTranslation("en", "Subject", "<p>Hello {{ UserName }}</p>", null, _userId);
        if (published)
        {
            template.Publish(_userId);
            template.ClearDomainEvents();
        }

        return template;
    }

    private NotificationRenderingService CreateRealRenderer()
    {
        var layoutRepo = new Mock<INotificationLayoutRepository>();
        layoutRepo
            .Setup(r => r.GetPublishedAsync(NotificationChannelType.Email, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationLayoutRenderSource(
                Guid.NewGuid(), null, "<html>{{ content | raw }}</html>", "{}"));

        return new NotificationRenderingService(
            new TemplateCache(new MemoryCache(new MemoryCacheOptions())),
            new Mock<INotificationTemplateRepository>().Object,
            layoutRepo.Object,
            new Mock<IUserRepository>().Object,
            new Mock<IApplicationRepository>().Object,
            new Mock<IPlatformSettingsRepository>().Object,
            new FluidTemplateRenderer(),
            TestHelpers.CreateOptions(new EmailSettings()),
            new Mock<ILogger<NotificationRenderingService>>().Object);
    }

    #region Create

    [Fact]
    public async Task Create_DuplicateScope_ReturnsConflict()
    {
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType());
        _templateRepoMock
            .Setup(r => r.ExistsAsync(_typeId, null, NotificationChannelType.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            new Mock<ILogger<CreateNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new CreateNotificationTemplateCommand(_typeId, null, NotificationChannelType.Email, "en")
            { CreatedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.DuplicateTemplate");
    }

    #endregion

    #region Update draft

    [Fact]
    public async Task UpdateDraft_BrokenLiquidSyntax_IsRejectedAtSaveTime()
    {
        var template = CreateAggregate();
        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var handler = new UpdateNotificationTemplateDraftCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            new FluidTemplateRenderer(),
            new Mock<ILogger<UpdateNotificationTemplateDraftCommandHandler>>().Object);

        var result = await handler.Handle(
            new UpdateNotificationTemplateDraftCommand(
                template.Id,
                [new DraftTranslationInput("en", "Subject", "{% if %} broken")])
            { ModifiedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.InvalidTemplateSyntax");
        _templateRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<NotificationTemplate>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateDraft_StaleExpectedModifiedAt_ReturnsConcurrencyConflict()
    {
        var template = CreateAggregate();
        // Simulate a later edit by someone else.
        template.UpsertTranslation("ar", "S", "<p>B</p>", null, Guid.NewGuid());
        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var handler = new UpdateNotificationTemplateDraftCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            new FluidTemplateRenderer(),
            new Mock<ILogger<UpdateNotificationTemplateDraftCommandHandler>>().Object);

        var result = await handler.Handle(
            new UpdateNotificationTemplateDraftCommand(
                template.Id,
                [new DraftTranslationInput("en", "Subject", "<p>B</p>")],
                ExpectedModifiedAt: template.ModifiedAt!.Value.AddMinutes(-5))
            { ModifiedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.ConcurrencyConflict");
    }

    #endregion

    #region Publish gate

    [Fact]
    public async Task Publish_DraftReferencingUnknownVariable_IsBlocked()
    {
        var template = CreateAggregate(published: false);
        // Draft references a variable absent from the sample data.
        template.UpsertTranslation("en", "Hi {{ Typo }}", "<p>{{ UserName }}</p>", null, _userId);

        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType(sampleDataJson: "{\"UserName\":\"Jane\"}"));

        var handler = new PublishNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            CreateRealRenderer(), _cacheInvalidatorMock.Object, _eventDispatcherMock.Object,
            new Mock<ILogger<PublishNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new PublishNotificationTemplateCommand(template.Id) { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.UnknownVariables");
        result.FirstError.Description.Should().StartWith("[en]");
        template.IsPublished.Should().BeFalse();
        _templateRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<NotificationTemplate>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Publish_ValidDraft_MovesPointerInvalidatesCacheAndDispatchesEvents()
    {
        var template = CreateAggregate(published: false);
        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType(sampleDataJson: "{\"UserName\":\"Jane\"}"));

        var handler = new PublishNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            CreateRealRenderer(), _cacheInvalidatorMock.Object, _eventDispatcherMock.Object,
            new Mock<ILogger<PublishNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new PublishNotificationTemplateCommand(template.Id) { PublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.PublishedVersionId.Should().NotBeNull();
        result.Value.DraftVersionId.Should().BeNull();
        _cacheInvalidatorMock.Verify(
            c => c.InvalidateTemplate("password-reset", NotificationChannelType.Email, null),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(template, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region System-global protection

    [Fact]
    public async Task Unpublish_SystemGlobalTemplate_ReturnsForbidden()
    {
        var template = CreateAggregate(applicationId: null);
        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType(isSystem: true));

        var handler = new UnpublishNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            _cacheInvalidatorMock.Object, _eventDispatcherMock.Object,
            new Mock<ILogger<UnpublishNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new UnpublishNotificationTemplateCommand(template.Id) { UnpublishedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.CannotUnpublishSystemTemplate");
        template.IsPublished.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_SystemGlobalTemplate_ReturnsForbidden()
    {
        var template = CreateAggregate(applicationId: null);
        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType(isSystem: true));

        var handler = new DeleteNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _cacheInvalidatorMock.Object,
            new Mock<ILogger<DeleteNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new DeleteNotificationTemplateCommand(template.Id) { DeletedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.CannotDeleteSystemGlobalTemplate");
        _templateRepoMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Delete_AppScopedOverrideOfSystemType_Succeeds()
    {
        var template = CreateAggregate(applicationId: Guid.NewGuid());
        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType(isSystem: true));

        var handler = new DeleteNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _cacheInvalidatorMock.Object,
            new Mock<ILogger<DeleteNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new DeleteNotificationTemplateCommand(template.Id) { DeletedBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _templateRepoMock.Verify(
            r => r.DeleteAsync(template.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Rollback

    [Fact]
    public async Task Rollback_ToPreviousVersion_RepointsAndInvalidatesCache()
    {
        var template = CreateAggregate();
        var v1Id = template.PublishedVersionId!.Value;
        template.UpsertTranslation("en", "Subject v2", "<p>v2</p>", null, _userId);
        template.Publish(_userId);
        template.ClearDomainEvents();

        _templateRepoMock.Setup(r => r.GetByIdAsync(template.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _typeRepoMock.Setup(r => r.GetByIdAsync(_typeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateType());

        var handler = new RollbackNotificationTemplateCommandHandler(
            _templateRepoMock.Object, _typeRepoMock.Object, _appRepoMock.Object,
            _cacheInvalidatorMock.Object, _eventDispatcherMock.Object,
            new Mock<ILogger<RollbackNotificationTemplateCommandHandler>>().Object);

        var result = await handler.Handle(
            new RollbackNotificationTemplateCommand(template.Id, v1Id) { RolledBackBy = _userId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        template.PublishedVersionId.Should().Be(v1Id);
        _cacheInvalidatorMock.Verify(
            c => c.InvalidateTemplate("password-reset", NotificationChannelType.Email, null),
            Times.Once);
    }

    #endregion
}
