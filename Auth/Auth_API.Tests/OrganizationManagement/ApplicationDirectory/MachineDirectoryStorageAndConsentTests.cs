using Auth.Application.Features.MachineKeys.MachineAccess;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth.Infrastructure.Persistence;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.OrganizationManagement.ApplicationDirectory;

/// <summary>
/// The machine directory's storage, consent and log (OI-202): the members query a machine read
/// runs (read through <see cref="RecordingDbConnectionFactory"/>, so Dapper builds the real
/// commands), the consent on the application and its command, the audit rows, and the log line.
/// </summary>
public class MachineDirectoryStorageAndConsentTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static string Normalized(string sql) => System.Text.RegularExpressions.Regex.Replace(sql.Trim(), @"\s+", " ");

    private static string Where(RecordedCommand command)
    {
        var text = command.CommandText;
        var from = text.IndexOf("WHERE", StringComparison.Ordinal);
        var to = text.LastIndexOf("ORDER BY", StringComparison.Ordinal);
        return Normalized(to < from ? text[from..] : text[from..to]);
    }

    // --- V4 + T8: only the holders of this application's roles, counted as listed ---

    [Fact]
    public async Task MachineMembers_AreTheDirectorysMembers_WhoHoldAnActiveUnexpiredRoleOfTheApplication_InCountAndPage()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var applicationId = Guid.NewGuid();

        await new OrganizationRepository(db).GetActiveMembersHoldingApplicationRolePagedAsync(
            Guid.NewGuid(), applicationId, Now, 2, 25, CancellationToken.None);

        db.Commands.Should().HaveCount(2, "a count, then the page");
        var (count, page) = (db.Commands[0], db.Commands[1]);
        count.CommandText.Should().Contain("COUNT(1)");

        const string expected =
            "WHERE ou.[OrganizationId] = @OrganizationId AND ou.[IsActive] = 1 "
            + "AND (ou.[ExpiresAt] IS NULL OR ou.[ExpiresAt] > @Now) AND u.[IsDeleted] = 0 "
            + "AND EXISTS ( SELECT 1 FROM [dbo].[OrganizationUserRoles] our "
            + "INNER JOIN [dbo].[Roles] r ON our.[RoleId] = r.[Id] "
            + "WHERE our.[OrganizationId] = @OrganizationId AND our.[ApplicationId] = @ApplicationId "
            + "AND r.[ApplicationId] = @ApplicationId AND r.[IsActive] = 1 "
            + "AND our.[UserId] = ou.[UserId] AND our.[IsActive] = 1 "
            + "AND (our.[ExpiresAt] IS NULL OR our.[ExpiresAt] > @Now))";
        Where(count).Should().Be(expected, "the total counts exactly the members the pages can show");
        Where(page).Should().Be(expected);
        Normalized(page.CommandText).Should().EndWith(
            expected + " ORDER BY ou.[JoinedAt], ou.[Id] OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");

        foreach (var command in new[] { count, page })
        {
            command.Parameters["ApplicationId"].Should().Be(applicationId);
            command.Parameters["Now"].Should().Be(Now, "the caller's clock, never the server's");
            command.ParameterTypes["Now"].Should().Be(System.Data.DbType.DateTime2);
        }

        page.Parameters["Offset"].Should().Be(25);
        page.Parameters["PageSize"].Should().Be(25);
    }

    [Fact]
    public async Task TheMembershipRule_AndTheRoleRule_AreTheOnesTheUserDoorAlreadyUses()
    {
        // One rule, two uses: the narrowing is the user door's member WHERE plus the user door's
        // role WHERE, correlated on the member instead of the page's id list.
        var userDoor = new RecordingDbConnectionFactory(affectedRows: 0);
        await new OrganizationRepository(userDoor).GetActiveMembersPagedAsync(Guid.NewGuid(), Now, 1, 20, CancellationToken.None);
        var roles = new RecordingDbConnectionFactory(affectedRows: 0);
        await new OrganizationRepository(roles).GetMemberApplicationRolesAsync(
            Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], Now, CancellationToken.None);
        var machine = new RecordingDbConnectionFactory(affectedRows: 0);
        await new OrganizationRepository(machine).GetActiveMembersHoldingApplicationRolePagedAsync(
            Guid.NewGuid(), Guid.NewGuid(), Now, 1, 20, CancellationToken.None);

        var memberRule = Where(userDoor.Commands[0]);
        var roleRule = Normalized(roles.Commands.Single().CommandText);
        roleRule = roleRule[roleRule.IndexOf("WHERE", StringComparison.Ordinal)..roleRule.IndexOf(" ORDER BY", StringComparison.Ordinal)]
            .Replace("our.[UserId] IN (@UserIds1)", "our.[UserId] = ou.[UserId]");

        Where(machine.Commands[0]).Should().StartWith(memberRule + " AND EXISTS (");
        Where(machine.Commands[0]).Should().Contain(roleRule);
    }

    // --- T2: the consent on the application ---

    [Fact]
    public void TheConsent_IsOnByDefault_ForANewApplication_AndAReadOne()
    {
        AppEntity.Create("CRM", "Example CRM", null, null, Guid.NewGuid()).AllowMachineOrganizationsRead.Should().BeTrue();
        TestHelpers.CreateApplication().AllowMachineOrganizationsRead.Should().BeTrue();
        TestHelpers.CreateApplication().ConsentedMachineScopes.Should().Equal(MachineClients.Scopes.OrganizationsReadAll);
    }

    [Fact]
    public void SwitchingTheConsent_ChangesItOnce_AndStampsWhoDidIt()
    {
        var application = TestHelpers.CreateApplication();
        var admin = Guid.NewGuid();

        application.SetMachineOrganizationsRead(true, admin).Should().BeFalse("already on: nothing to change");
        application.SetMachineOrganizationsRead(false, admin).Should().BeTrue();
        application.AllowMachineOrganizationsRead.Should().BeFalse();
        application.ConsentedMachineScopes.Should().BeEmpty();
        application.ModifiedBy.Should().Be(admin);
    }

    [Fact]
    public async Task TheConsentWrite_IsItsOwnStatement_AndUpdateAsyncNeverWritesIt()
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 1);
        var application = TestHelpers.CreateApplication();
        application.SetMachineOrganizationsRead(false, Guid.NewGuid());
        var repository = new ApplicationRepository(db);

        (await repository.UpdateMachineOrganizationsReadAsync(application, CancellationToken.None)).Should().BeTrue();
        await repository.UpdateAsync(application, CancellationToken.None);

        var consent = db.Commands[0];
        Normalized(consent.CommandText).Should().StartWith(
            "UPDATE [dbo].[Applications] SET [AllowMachineOrganizationsRead] = @AllowMachineOrganizationsRead, [ModifiedAt] = @ModifiedAt, [ModifiedBy] = @ModifiedBy WHERE [Id] = @Id AND [IsDeleted] = 0");
        consent.Parameters["AllowMachineOrganizationsRead"].Should().Be(false);
        db.Commands.Skip(1).Should().OnlyContain(command => !command.CommandText.Contains("AllowMachineOrganizationsRead"),
            "a settings save built from an older copy can never switch the consent back");
    }

    // --- V8: the command, its event and its audit rows ---

    private static (SetMachineOrganizationsReadCommandHandler Handler, Mock<IApplicationRepository> Applications, Mock<IPublisher> Publisher) Command(AppEntity? application)
    {
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(application);
        applications.Setup(r => r.UpdateMachineOrganizationsReadAsync(It.IsAny<AppEntity>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var publisher = new Mock<IPublisher>();
        var handler = new SetMachineOrganizationsReadCommandHandler(
            applications.Object, publisher.Object, new FixedTimeProvider(new DateTimeOffset(Now)),
            NullLogger<SetMachineOrganizationsReadCommandHandler>.Instance);
        return (handler, applications, publisher);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCommand_WritesTheConsent_AndRaisesOneEvent_OnAndOff(bool allowed)
    {
        var application = TestHelpers.CreateApplication();
        application.SetMachineOrganizationsRead(!allowed, Guid.NewGuid());
        var admin = Guid.NewGuid();
        var (handler, applications, publisher) = Command(application);

        var result = await handler.Handle(
            new SetMachineOrganizationsReadCommand(application.Id, allowed) { ChangedBy = admin }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.OrganizationsReadAll.Should().Be(allowed);
        applications.Verify(r => r.UpdateMachineOrganizationsReadAsync(
            It.Is<AppEntity>(a => a.AllowMachineOrganizationsRead == allowed), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.Publish(
            It.Is<MachineOrganizationsReadChangedEvent>(e => e.ApplicationId == application.Id && e.Allowed == allowed && e.TriggeredBy == admin),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheCommand_ForTheValueAlreadyStored_WritesNothing_AndRaisesNothing()
    {
        var (handler, applications, publisher) = Command(TestHelpers.CreateApplication());

        var result = await handler.Handle(new SetMachineOrganizationsReadCommand(Guid.NewGuid(), true), CancellationToken.None);

        result.Value.OrganizationsReadAll.Should().BeTrue();
        applications.Verify(r => r.UpdateMachineOrganizationsReadAsync(It.IsAny<AppEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        publisher.Verify(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
        publisher.Verify(p => p.Publish(It.IsAny<MachineOrganizationsReadChangedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheCommand_ForAnUnknownApplication_IsNotFound()
    {
        var (handler, _, _) = Command(null);

        var result = await handler.Handle(new SetMachineOrganizationsReadCommand(Guid.NewGuid(), false), CancellationToken.None);

        result.FirstError.Code.Should().Be(ApplicationErrors.NotFound(Guid.Empty).Code);
    }

    [Theory]
    [InlineData(true, AuditActions.MachineOrganizationsReadAllowed)]
    [InlineData(false, AuditActions.MachineOrganizationsReadWithdrawn)]
    public async Task TheAuditHandler_WritesOneRow_NamingTheChange(bool allowed, string action)
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLog log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);
        var applicationId = Guid.NewGuid();
        var admin = Guid.NewGuid();

        await new MachineOrganizationsReadChangedAuditEventHandler(audit.Object, NullLogger<MachineOrganizationsReadChangedAuditEventHandler>.Instance)
            .Handle(new MachineOrganizationsReadChangedEvent(Guid.NewGuid(), applicationId, allowed, admin, Now), CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(action);
        written.ActionType.Should().Be(AuditActionTypes.ApiKeyManagement);
        AuditActions.ByCode[action].Should().Be(AuditActionTypes.ApiKeyManagement);
        written.ApplicationId.Should().Be(applicationId);
        written.PerformedBy.Should().Be(admin);
        written.EntityId.Should().Be(applicationId);
    }

    // --- T10: the log line names the caller kind ---

    [Fact]
    public async Task AMachineRead_LogsOneLine_AsMachine_AndNoPersonalData()
    {
        var application = TestHelpers.CreateApplication(code: "crm");
        var organization = TestHelpers.CreateOrganization();
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByCodeAsync("crm", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var organizations = new Mock<IOrganizationRepository>();
        organizations.Setup(r => r.GetByIdAsync(organization.Id, It.IsAny<CancellationToken>())).ReturnsAsync(organization);
        organizations.Setup(r => r.IsApplicationEnabledAsync(organization.Id, application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        organizations.Setup(r => r.GetActiveMembersHoldingApplicationRolePagedAsync(
                organization.Id, application.Id, Now, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([new ApplicationDirectoryMemberRow(Guid.NewGuid(), Guid.NewGuid(), "Rae", "Reader", "reader@example.com", null, "org-member", Now)], 1));
        organizations.Setup(r => r.GetMemberApplicationRolesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var logger = new CapturingLogger<GetApplicationOrganizationMembersQueryHandler>();
        var handler = new GetApplicationOrganizationMembersQueryHandler(
            new ApplicationDirectoryAccess(Mock.Of<IUserRepository>(MockBehavior.Strict), applications.Object, organizations.Object),
            organizations.Object,
            Mock.Of<IImageUrlComposer>(),
            new FixedTimeProvider(new DateTimeOffset(Now)),
            logger);

        var result = await handler.Handle(
            new GetApplicationOrganizationMembersQuery(new ApplicationMachineCaller("crm", "organizations.read.all"), organization.Id),
            CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : null);
        result.Value.Items.Single().ApplicationRoles.Should().NotBeNull().And.BeEmpty("always present for a machine read");
        var line = logger.Lines.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().Contain("crm").And.Contain(organization.Id.ToString()).And.Contain("members")
            .And.Contain("as machine").And.Contain("(1 items)");
        line.Message.Should().NotContain("reader@example.com").And.NotContain("Rae").And.NotContain("Reader");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }
}
