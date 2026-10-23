using Auth.Application.Features.Users.RemoveUserRole;
using Auth.Application.Features.Users.RevokeUserPermission;
using Auth_API.Modules.UserManagement.Controllers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.UserManagement;

/// <summary>
/// The removal actions hand the assignment's scope to their commands. Both commands
/// always had an optional <c>ApplicationId</c> and the actions never passed it, so
/// every removal targeted the platform scope: an application-scoped role or grant was
/// reported removed and stayed. The argument is optional, so an action that stopped
/// passing it again would still compile and every handler test would still pass; only
/// this reaches the action. Deliberate break: drop <c>applicationId</c> from either
/// command's construction.
/// </summary>
public class AssignmentRemovalScopeControllerTests
{
    private static UsersController Controller(Mock<ISender> sender) =>
        new(sender.Object) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveRole_PassesTheScopeOrItsAbsenceToTheCommand(bool applicationScoped)
    {
        Guid? applicationId = applicationScoped ? Guid.NewGuid() : null;
        var sender = new Mock<ISender>();
        RemoveUserRoleCommand? sent = null;
        sender
            .Setup(s => s.Send(It.IsAny<RemoveUserRoleCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<bool>>, CancellationToken>((command, _) => sent = (RemoveUserRoleCommand)command)
            .ReturnsAsync((ErrorOr<bool>)true);

        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var response = await Controller(sender).RemoveRole(userId, roleId, applicationId, CancellationToken.None);

        response.Should().BeOfType<NoContentResult>();
        sent.Should().NotBeNull();
        sent!.UserId.Should().Be(userId);
        sent.RoleId.Should().Be(roleId);
        sent.ApplicationId.Should().Be(applicationId, "a missing scope means the platform-wide assignment, and only then");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RevokePermission_PassesTheScopeOrItsAbsenceToTheCommand(bool applicationScoped)
    {
        Guid? applicationId = applicationScoped ? Guid.NewGuid() : null;
        var sender = new Mock<ISender>();
        RevokeUserPermissionCommand? sent = null;
        sender
            .Setup(s => s.Send(It.IsAny<RevokeUserPermissionCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<ErrorOr<bool>>, CancellationToken>((command, _) => sent = (RevokeUserPermissionCommand)command)
            .ReturnsAsync((ErrorOr<bool>)true);

        var userId = Guid.NewGuid();
        var permissionId = Guid.NewGuid();
        var response = await Controller(sender).RevokePermission(userId, permissionId, applicationId, CancellationToken.None);

        response.Should().BeOfType<NoContentResult>();
        sent.Should().NotBeNull();
        sent!.UserId.Should().Be(userId);
        sent.PermissionId.Should().Be(permissionId);
        sent.ApplicationId.Should().Be(applicationId, "a missing scope means the platform-wide grant, and only then");
    }
}
