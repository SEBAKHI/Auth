using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ApplicationEntity = Auth.Domain.Entities.Application;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// S23, AM11 (11-1) and (11-2): the administrator surface of an application's two-step
/// requirement. An update that omits the requirement or the access mode leaves them as
/// they are (it used to switch the requirement off, and close the application down, without
/// a trace); a change is published after the save, once, and audited; an application born
/// requiring two steps is audited too.
/// </summary>
/// <remarks>
/// Deliberate breaks: drop the dispatch from the create handler (the "create with the
/// requirement" test turns red); default the omitted requirement to false again (the
/// "omitted" test turns red).
/// </remarks>
public class ApplicationTwoFactorRequirementCommandTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class Harness
    {
        public Mock<IApplicationRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<ApplicationTwoFactorRequirementChangedEvent> Published { get; } = [];
        public List<CancellationToken> PublishTokens { get; } = [];
        public ApplicationEntity Application { get; }

        public Harness(bool requireTwoFactor = false, ApplicationAccessMode accessMode = ApplicationAccessMode.Everyone)
        {
            Application = TestHelpers.CreateApplication(code: "CRM", name: "CRM", accessMode: accessMode);
            if (requireTwoFactor)
            {
                Application.Update("CRM", null, Application.BaseUrl, null, null, null, false, true, false, 60, 5, accessMode, Admin);
                Application.ClearDomainEvents();
            }

            Repository.Setup(r => r.GetByIdAsync(Application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Repository.Setup(r => r.ExistsByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Repository.Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);
            Publisher.Setup(p => p.Publish(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
                .Callback((IDomainEvent notification, CancellationToken token) =>
                {
                    if (notification is ApplicationTwoFactorRequirementChangedEvent e) Published.Add(e);
                    PublishTokens.Add(token);
                })
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<ApplicationDto>> Update(bool? requireTwoFactor, ApplicationAccessMode? accessMode = null)
        {
            var permissions = new Mock<IPermissionRepository>();
            return new UpdateApplicationCommandHandler(
                    Repository.Object,
                    new Mock<ICredentialRevocationService>().Object,
                    ApplicationTestImages.Composer(),
                    new Auth.Application.Common.OrganizationCreatorRoleCheck(new Mock<IRoleRepository>().Object, permissions.Object),
                    new Auth.Application.Common.PermissionGrantGuard(permissions.Object),
                    ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
                    Publisher.Object,
                    new Auth.Infrastructure.MediatRDomainEventDispatcher(Publisher.Object),
                    new Mock<ILogger<UpdateApplicationCommandHandler>>().Object)
                .Handle(
                    new UpdateApplicationCommand(
                        Application.Id, "CRM", BaseUrl: Application.BaseUrl,
                        RequireTwoFactor: requireTwoFactor, AccessMode: accessMode)
                    {
                        ModifiedBy = Admin
                    },
                    CancellationToken.None);
        }

        public Task<ErrorOr<ApplicationDto>> Create(bool requireTwoFactor) =>
            new CreateApplicationCommandHandler(
                    Repository.Object,
                    ApplicationTestImages.Composer(),
                    ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
                    Publisher.Object,
                    new Auth.Infrastructure.MediatRDomainEventDispatcher(Publisher.Object),
                    new Mock<ILogger<CreateApplicationCommandHandler>>().Object)
                .Handle(
                    new CreateApplicationCommand("CRM", "CRM", RequireTwoFactor: requireTwoFactor) { CreatedBy = Admin },
                    CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_UpdateOmittingTheRequirement_LeavesItAsItIs_AndRecordsNothing(bool stored)
    {
        var harness = new Harness(requireTwoFactor: stored);

        var result = await harness.Update(requireTwoFactor: null);

        result.Value.RequireTwoFactor.Should().Be(stored);
        harness.Application.RequireTwoFactor.Should().Be(stored);
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UpdateOmittingTheAccessMode_LeavesItOpen()
    {
        // Omitted used to mean Restricted: one forgotten field closed an open application down.
        var harness = new Harness(accessMode: ApplicationAccessMode.Everyone);

        var result = await harness.Update(requireTwoFactor: null, accessMode: null);

        result.Value.AccessMode.Should().Be(ApplicationAccessMode.Everyone);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Handle_UpdateChangingTheRequirement_PublishesOneChange_AfterTheSave(bool stored, bool requested)
    {
        var harness = new Harness(requireTwoFactor: stored);
        var order = new List<string>();
        harness.Repository.Setup(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).Returns(Task.CompletedTask);
        harness.Publisher.Setup(p => p.Publish(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()))
            .Callback((IDomainEvent notification, CancellationToken token) =>
            {
                order.Add("publish");
                if (notification is ApplicationTwoFactorRequirementChangedEvent e) harness.Published.Add(e);
                harness.PublishTokens.Add(token);
            })
            .Returns(Task.CompletedTask);

        (await harness.Update(requireTwoFactor: requested)).IsError.Should().BeFalse();

        var published = harness.Published.Should().ContainSingle().Subject;
        published.RequireTwoFactor.Should().Be(requested);
        published.ApplicationId.Should().Be(harness.Application.Id);
        published.TriggeredBy.Should().Be(Admin);
        order.Should().Equal("save", "publish");
        harness.PublishTokens.Should().AllSatisfy(token => token.Should().Be(CancellationToken.None));
    }

    [Fact]
    public async Task Handle_UpdateResendingTheSameValue_PublishesNothing()
    {
        var harness = new Harness(requireTwoFactor: true);

        (await harness.Update(requireTwoFactor: true)).IsError.Should().BeFalse();

        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CreateWithTheRequirement_IsAudited()
    {
        var harness = new Harness();

        var result = await harness.Create(requireTwoFactor: true);

        result.Value.RequireTwoFactor.Should().BeTrue();
        harness.Published.Should().ContainSingle().Which.RequireTwoFactor.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CreateWithoutTheRequirement_PublishesNothing()
    {
        var harness = new Harness();

        (await harness.Create(requireTwoFactor: false)).IsError.Should().BeFalse();

        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AuditHandler_WritesTheChange_AgainstTheApplication_WithNoUser()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);
        var applicationId = Guid.NewGuid();

        await new ApplicationTwoFactorRequirementChangedAuditEventHandler(
                audit.Object, NullLogger<ApplicationTwoFactorRequirementChangedAuditEventHandler>.Instance)
            .Handle(
                new ApplicationTwoFactorRequirementChangedEvent(
                    Guid.NewGuid(), applicationId, "CRM", RequireTwoFactor: true, Admin, DateTimeOffset.UtcNow),
                CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.ApplicationTwoFactorRequirementChanged);
        written.EntityType.Should().Be("Application");
        written.EntityId.Should().Be(applicationId);
        written.UserId.Should().BeNull("the row is about the application, not a user");
        written.PerformedBy.Should().Be(Admin);
        using var data = JsonDocument.Parse(written.AdditionalData!);
        data.RootElement.GetProperty("requireTwoFactor").GetBoolean().Should().BeTrue();
        data.RootElement.GetProperty("applicationCode").GetString().Should().Be("CRM");
    }

    [Fact]
    public async Task AuditHandler_AFailedWrite_IsLoggedNeverThrown()
    {
        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database"));

        var act = () => new ApplicationTwoFactorRequirementChangedAuditEventHandler(
                audit.Object, NullLogger<ApplicationTwoFactorRequirementChangedAuditEventHandler>.Instance)
            .Handle(
                new ApplicationTwoFactorRequirementChangedEvent(
                    Guid.NewGuid(), Guid.NewGuid(), "CRM", RequireTwoFactor: false, Admin, DateTimeOffset.UtcNow),
                CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
