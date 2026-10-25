using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Authentication;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// OI-231 T2 (V3): a member removal leaves one audit row — the action, the removed person
/// as subject, the administrator as actor, and in serialized details the organization and
/// every application role and permission that went with the membership, surviving a quote
/// in a role's name. The removal has committed by then, so a failed write is logged once
/// and swallowed; the handler's own cancellation still passes through.
/// </summary>
/// <remarks>
/// Deliberate breaks: build the details by interpolation; remove the catch.
/// </remarks>
public class OrganizationMemberRemovedAuditEventHandlerTests
{
    private const string QuotedRoleName = "Agent \"lead\" \\ east";

    private readonly Mock<IAuditLogRepository> _audit = new();

    private static OrganizationMemberRemovedEvent Removed() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        [new RemovedOrganizationApplicationRole(Guid.NewGuid(), Guid.NewGuid(), "crm:agent", QuotedRoleName)],
        [new RemovedOrganizationPermission(Guid.NewGuid(), Guid.NewGuid(), "crm:tickets:read")]);

    [Fact]
    public async Task Removed_WritesOneRow_ListingWhatWentWithTheMembership()
    {
        var notification = Removed();
        AuditLog? row = null;
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => row = log)
            .Returns(Task.CompletedTask);

        await new OrganizationMemberRemovedAuditEventHandler(
                _audit.Object, new LockSignalTests.CapturingLogger<OrganizationMemberRemovedAuditEventHandler>())
            .Handle(notification, CancellationToken.None);

        _audit.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
        row!.Action.Should().Be(AuditActions.OrganizationMemberRemoved).And.Be("organization.member_removed");
        row.ActionType.Should().Be(AuditActionTypes.Authorization);
        row.UserId.Should().Be(notification.UserId);
        row.PerformedBy.Should().Be(notification.RemovedBy);
        row.EntityType.Should().Be("OrganizationUser");

        using var data = JsonDocument.Parse(row.AdditionalData!);
        data.RootElement.GetProperty("organizationId").GetGuid().Should().Be(notification.OrganizationId);
        var role = data.RootElement.GetProperty("removedApplicationRoles").EnumerateArray().Should().ContainSingle().Subject;
        role.GetProperty("applicationId").GetGuid().Should().Be(notification.RemovedApplicationRoles[0].ApplicationId);
        role.GetProperty("roleId").GetGuid().Should().Be(notification.RemovedApplicationRoles[0].RoleId);
        role.GetProperty("roleCode").GetString().Should().Be("crm:agent");
        role.GetProperty("roleName").GetString().Should().Be(QuotedRoleName);
        var permission = data.RootElement.GetProperty("removedPermissions").EnumerateArray().Should().ContainSingle().Subject;
        permission.GetProperty("permissionId").GetGuid().Should().Be(notification.RemovedPermissions[0].PermissionId);
        permission.GetProperty("permissionCode").GetString().Should().Be("crm:tickets:read");
    }

    [Fact]
    public async Task Removed_WhenTheWriteFails_LogsOnceAndDoesNotThrow()
    {
        var log = new LockSignalTests.CapturingLogger<OrganizationMemberRemovedAuditEventHandler>();
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var notification = Removed();

        var act = () => new OrganizationMemberRemovedAuditEventHandler(_audit.Object, log).Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var line = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        line.Values["UserId"].Should().Be(notification.UserId);
        line.Values["OrganizationId"].Should().Be(notification.OrganizationId);
        line.Values["RemovedBy"].Should().Be(notification.RemovedBy);
    }

    [Fact]
    public async Task WhenItsOwnTokenIsCancelled_TheCancellationPassesThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var log = new LockSignalTests.CapturingLogger<OrganizationMemberRemovedAuditEventHandler>();
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => new OrganizationMemberRemovedAuditEventHandler(_audit.Object, log).Handle(Removed(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        log.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }
}
