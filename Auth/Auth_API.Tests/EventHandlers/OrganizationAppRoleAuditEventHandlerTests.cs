using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Authentication;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// OI-225 (1): an organization application role given or taken leaves an audit row —
/// the action, the member as subject, the administrator as actor, the application, and
/// the organization and role in serialized details that survive a quote in the role's
/// name. The change has committed by then, so a failed write is logged once and
/// swallowed; the handler's own cancellation still passes through.
/// </summary>
/// <remarks>
/// Deliberate breaks: build the details by interpolation again; let the repository's
/// exception escape the handler.
/// </remarks>
public class OrganizationAppRoleAuditEventHandlerTests
{
    private const string QuotedRoleName = "Agent \"lead\" \\ east";

    private readonly Mock<IAuditLogRepository> _audit = new();

    private static OrganizationAppRoleAssignedEvent Assigned() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), QuotedRoleName,
        new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.NewGuid());

    private static OrganizationAppRoleRemovedEvent Removed() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), QuotedRoleName, Guid.NewGuid());

    private async Task<AuditLog> Write<TEvent>(INotificationHandler<TEvent> handler, TEvent notification)
        where TEvent : INotification
    {
        AuditLog? written = null;
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);

        await handler.Handle(notification, CancellationToken.None);

        written.Should().NotBeNull();
        return written!;
    }

    [Fact]
    public async Task Assigned_WritesTheRow()
    {
        var notification = Assigned();

        var row = await Write(
            new OrganizationAppRoleAssignedAuditEventHandler(_audit.Object, new LockSignalTests.CapturingLogger<OrganizationAppRoleAssignedAuditEventHandler>()),
            notification);

        row.Action.Should().Be(AuditActions.OrganizationAppRoleAssigned).And.Be("organization.app_role_assigned");
        row.ActionType.Should().Be(AuditActionTypes.Authorization);
        row.UserId.Should().Be(notification.UserId);
        row.PerformedBy.Should().Be(notification.AssignedBy);
        row.ApplicationId.Should().Be(notification.ApplicationId);
        using var data = JsonDocument.Parse(row.AdditionalData!);
        data.RootElement.GetProperty("organizationId").GetGuid().Should().Be(notification.OrganizationId);
        data.RootElement.GetProperty("roleId").GetGuid().Should().Be(notification.RoleId);
        data.RootElement.GetProperty("roleName").GetString().Should().Be(QuotedRoleName);
        data.RootElement.GetProperty("expiresAt").GetDateTime().Should().Be(notification.ExpiresAt!.Value);
    }

    [Fact]
    public async Task Removed_WritesTheRow()
    {
        var notification = Removed();

        var row = await Write(
            new OrganizationAppRoleRemovedAuditEventHandler(_audit.Object, new LockSignalTests.CapturingLogger<OrganizationAppRoleRemovedAuditEventHandler>()),
            notification);

        row.Action.Should().Be(AuditActions.OrganizationAppRoleRemoved).And.Be("organization.app_role_removed");
        row.ActionType.Should().Be(AuditActionTypes.Authorization);
        row.UserId.Should().Be(notification.UserId);
        row.PerformedBy.Should().Be(notification.RemovedBy);
        row.ApplicationId.Should().Be(notification.ApplicationId);
        using var data = JsonDocument.Parse(row.AdditionalData!);
        data.RootElement.GetProperty("organizationId").GetGuid().Should().Be(notification.OrganizationId);
        data.RootElement.GetProperty("roleId").GetGuid().Should().Be(notification.RoleId);
        data.RootElement.GetProperty("roleName").GetString().Should().Be(QuotedRoleName);
    }

    [Fact]
    public async Task Assigned_WhenTheWriteFails_LogsOnceAndDoesNotThrow()
    {
        var log = new LockSignalTests.CapturingLogger<OrganizationAppRoleAssignedAuditEventHandler>();
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var notification = Assigned();

        var act = () => new OrganizationAppRoleAssignedAuditEventHandler(_audit.Object, log).Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var line = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        line.Values["UserId"].Should().Be(notification.UserId);
        line.Values["RoleId"].Should().Be(notification.RoleId);
        line.Values["OrganizationId"].Should().Be(notification.OrganizationId);
    }

    [Fact]
    public async Task Removed_WhenTheWriteFails_LogsOnceAndDoesNotThrow()
    {
        var log = new LockSignalTests.CapturingLogger<OrganizationAppRoleRemovedAuditEventHandler>();
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var notification = Removed();

        var act = () => new OrganizationAppRoleRemovedAuditEventHandler(_audit.Object, log).Handle(notification, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var line = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        line.Values["UserId"].Should().Be(notification.UserId);
        line.Values["RoleId"].Should().Be(notification.RoleId);
    }

    [Fact]
    public async Task WhenItsOwnTokenIsCancelled_TheCancellationPassesThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var log = new LockSignalTests.CapturingLogger<OrganizationAppRoleRemovedAuditEventHandler>();
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => new OrganizationAppRoleRemovedAuditEventHandler(_audit.Object, log).Handle(Removed(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        log.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }
}
