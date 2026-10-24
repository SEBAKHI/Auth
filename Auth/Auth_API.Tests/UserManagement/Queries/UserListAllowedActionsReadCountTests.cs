using Auth.Application.Common;
using Auth.Application.Features.Users.GetUsers;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Persistence;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Queries;

/// <summary>
/// OI-224 V6 (bottleneck B1): a page of the user list tells the console what the
/// caller may do to every row from a fixed number of statements — the caller's
/// platform permissions, the page's, and who holds <c>*</c> — never one per row.
/// OI-225 (6): the page's roles are one more statement, not one per row. The
/// statements are counted on the real permission and role repositories.
/// </summary>
/// <remarks>
/// Deliberate breaks: read each row's platform permissions with the one-account
/// read again; read each row's roles with GetUserRolesAsync again.
/// </remarks>
public class UserListAllowedActionsReadCountTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Handle_WithACaller_ReadsFourStatements_WhateverThePageSize(int pageSize)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var permissions = new PermissionRepository(db);
        var roles = new RoleRepository(db);
        var policy = new AccountActionPolicy(
            permissions, Mock.Of<IUserRepository>(), TimeProvider.System, Mock.Of<ILogger<AccountActionPolicy>>());
        var users = new Mock<IUserRepository>();
        var page = Enumerable.Range(0, pageSize).Select(_ => TestHelpers.CreateUser()).ToList();
        users
            .Setup(r => r.GetPagedAsync(1, pageSize, null, null, It.IsAny<SortDirection>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((page, pageSize));

        var result = await new GetUsersQueryHandler(users.Object, roles, permissions, policy, Mock.Of<IImageUrlComposer>())
            .Handle(new GetUsersQuery(1, pageSize) { ActorId = Guid.NewGuid() }, CancellationToken.None);

        result.Value.Users.Should().HaveCount(pageSize).And.OnlyContain(user => user.AllowedActions != null);
        db.Commands.Should().HaveCount(4,
            "the page's roles, the caller's permissions, the page's in one statement, and the holders of *: the same four for 1 row or 100");
        var pageWide = db.Commands
            .Where(command => command.Parameters.Keys.Count(key => key.StartsWith("UserIds", StringComparison.Ordinal)) == pageSize)
            .ToList();
        pageWide.Should().HaveCount(2,
            "the page's roles and its platform permissions are each read for every row at once");
        pageWide.Should().ContainSingle(command => command.CommandText.Contains("SELECT ur.[UserId], r.* FROM [dbo].[Roles] r"),
            "the roles are one of the two page-wide reads");
    }
}
