using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
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
/// OI-192 review (S90-1): an accepted invitation sends the invitee to the application's
/// initiate-login URI, or to its base URL when there is none, so a change to either is audited
/// like a change to the redirect allowlists: one event after the save when either changed, with
/// what it held before and after and who saved it, and none when neither changed.
/// </summary>
/// <remarks>Deliberate break: remove the publish from the update handler; the "changed" tests turn red.</remarks>
public class ApplicationLaunchAddressAuditTests
{
    private const string Start = "https://crm.example.com/signin/start";
    private const string Home = "https://crm.example.com";
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class Harness
    {
        public Mock<IApplicationRepository> Repository { get; } = new();
        public Mock<IPublisher> Publisher { get; } = new();
        public List<ApplicationLaunchAddressesChangedEvent> Published { get; } = [];
        public ApplicationEntity Application { get; }

        public Harness()
        {
            Application = TestHelpers.CreateApplication(code: "CRM", name: "CRM", baseUrl: Home);
            Application.LoadInitiateLoginUri(Start);
            Repository.Setup(r => r.GetByIdAsync(Application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Application);
            Repository.Setup(r => r.ExistsByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Repository.Setup(r => r.CreateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ApplicationEntity app, CancellationToken _) => app);
            Publisher.Setup(p => p.Publish(It.IsAny<ApplicationLaunchAddressesChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((ApplicationLaunchAddressesChangedEvent e, CancellationToken _) => Published.Add(e))
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<ApplicationDto>> Update(string? baseUrl, string? initiateLoginUri)
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
                    new Mock<ILogger<UpdateApplicationCommandHandler>>().Object)
                .Handle(
                    new UpdateApplicationCommand(Application.Id, "CRM", BaseUrl: baseUrl, InitiateLoginUri: initiateLoginUri)
                    {
                        ModifiedBy = Admin
                    },
                    CancellationToken.None);
        }

        public Task<ErrorOr<ApplicationDto>> Create(string? baseUrl, string? initiateLoginUri) =>
            new CreateApplicationCommandHandler(
                    Repository.Object,
                    ApplicationTestImages.Composer(),
                    ApplicationTestImages.Guard(new Mock<IUploadedImageRepository>()),
                    Publisher.Object,
                    new Mock<ILogger<CreateApplicationCommandHandler>>().Object)
                .Handle(
                    new CreateApplicationCommand("CRM", "CRM", BaseUrl: baseUrl, InitiateLoginUri: initiateLoginUri)
                    {
                        CreatedBy = Admin
                    },
                    CancellationToken.None);
    }

    // --- update ---

    [Fact]
    public async Task Update_ThatChangesTheInitiateLoginUri_PublishesOneEventWithBeforeAndAfter()
    {
        var harness = new Harness();

        (await harness.Update(baseUrl: Home, initiateLoginUri: "https://crm.example.com/start-here")).IsError.Should().BeFalse();

        var published = harness.Published.Should().ContainSingle().Subject;
        published.ApplicationId.Should().Be(harness.Application.Id);
        published.ApplicationCode.Should().Be("CRM");
        published.InitiateLoginUri.Should().Be(new LaunchAddressChange(Start, "https://crm.example.com/start-here"));
        published.BaseUrl.Should().BeNull("the base URL was sent back unchanged");
        published.TriggeredBy.Should().Be(Admin);
    }

    [Fact]
    public async Task Update_ThatRemovesTheInitiateLoginUri_RecordsTheRemoval()
    {
        var harness = new Harness();

        await harness.Update(baseUrl: Home, initiateLoginUri: "");

        harness.Published.Should().ContainSingle()
            .Which.InitiateLoginUri.Should().Be(new LaunchAddressChange(Start, null));
    }

    [Fact]
    public async Task Update_ThatChangesTheBaseUrl_PublishesTheChange()
    {
        var harness = new Harness();

        await harness.Update(baseUrl: "https://desk.example.com", initiateLoginUri: null);

        var published = harness.Published.Should().ContainSingle().Subject;
        published.BaseUrl.Should().Be(new LaunchAddressChange(Home, "https://desk.example.com"));
        published.InitiateLoginUri.Should().BeNull("null on the request leaves it unchanged");
    }

    [Fact]
    public async Task Update_ThatChangesNeither_PublishesNothing()
    {
        var harness = new Harness();

        await harness.Update(baseUrl: Home, initiateLoginUri: null);
        await harness.Update(baseUrl: Home, initiateLoginUri: Start);

        harness.Published.Should().BeEmpty();
        harness.Publisher.Verify(
            p => p.Publish(It.IsAny<ApplicationLaunchAddressesChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_PublishesAfterTheSave_AndNotCancellable()
    {
        var harness = new Harness();
        var order = new List<string>();
        harness.Repository.Setup(r => r.UpdateAsync(It.IsAny<ApplicationEntity>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save"))
            .Returns(Task.CompletedTask);
        harness.Publisher.Setup(p => p.Publish(It.IsAny<ApplicationLaunchAddressesChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback((ApplicationLaunchAddressesChangedEvent _, CancellationToken token) =>
                order.Add(token.CanBeCanceled ? "publish:cancellable" : "publish"))
            .Returns(Task.CompletedTask);

        await harness.Update(baseUrl: Home, initiateLoginUri: "https://crm.example.com/start-here");

        order.Should().Equal("save", "publish");
    }

    // --- create ---

    [Fact]
    public async Task Create_WithLaunchAddresses_RecordsThemAsAChangeFromNone()
    {
        var harness = new Harness();

        await harness.Create(baseUrl: Home, initiateLoginUri: Start);

        var published = harness.Published.Should().ContainSingle().Subject;
        published.InitiateLoginUri.Should().Be(new LaunchAddressChange(null, Start));
        published.BaseUrl.Should().Be(new LaunchAddressChange(null, Home));
        published.TriggeredBy.Should().Be(Admin);
    }

    [Fact]
    public async Task Create_WithoutLaunchAddresses_PublishesNothing()
    {
        var harness = new Harness();

        await harness.Create(baseUrl: null, initiateLoginUri: null);

        harness.Published.Should().BeEmpty();
    }

    // --- the audit row ---

    private static ApplicationLaunchAddressesChangedEvent Changed() => new(
        Guid.NewGuid(), Guid.NewGuid(), "CRM",
        new LaunchAddressChange(Start, "https://crm.example.com/start-here"),
        null,
        Admin,
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task TheAuditRow_NamesEachChangedAddress_BeforeAndAfter()
    {
        var repository = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);
        var notification = Changed();

        await new ApplicationLaunchAddressesChangedAuditEventHandler(
                repository.Object, NullLogger<ApplicationLaunchAddressesChangedAuditEventHandler>.Instance)
            .Handle(notification, CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.ApplicationLaunchAddressesChanged);
        written.ActionType.Should().Be(AuditActionTypes.Application);
        written.PerformedBy.Should().Be(Admin);
        written.EntityId.Should().Be(notification.ApplicationId);
        written.ApplicationId.Should().Be(notification.ApplicationId);
        using var data = JsonDocument.Parse(written.AdditionalData!);
        data.RootElement.GetProperty("applicationCode").GetString().Should().Be("CRM");
        data.RootElement.GetProperty("initiateLoginUri").GetProperty("Before").GetString().Should().Be(Start);
        data.RootElement.GetProperty("initiateLoginUri").GetProperty("After").GetString()
            .Should().Be("https://crm.example.com/start-here");
        data.RootElement.GetProperty("baseUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task TheAuditHandler_SwallowsAndLogsAFailure()
    {
        // The save has committed: a throwing handler would turn it into an error answer.
        var repository = new Mock<IAuditLogRepository>();
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var logger = new Mock<ILogger<ApplicationLaunchAddressesChangedAuditEventHandler>>();

        var act = () => new ApplicationLaunchAddressesChangedAuditEventHandler(repository.Object, logger.Object)
            .Handle(Changed(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<InvalidOperationException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void LaunchAddressChange_IsNullForTheSameAddress_AndOrdinal()
    {
        LaunchAddressChange.Between(Start, Start).Should().BeNull();
        LaunchAddressChange.Between(null, null).Should().BeNull();
        LaunchAddressChange.Between(Start, Start.ToUpperInvariant()).Should().NotBeNull();
    }
}
