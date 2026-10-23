using Auth.Application.Common;
using Auth.Application.Features.Users.GetUsers;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
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
/// The statements are counted on the real permission repository.
/// </summary>
/// <remarks>
/// The list's role read per row predates OI-224 and is outside these counts (its
/// repository is a double here); see the deviation row of 2026-10-23. Deliberate
/// break: read each row's platform permissions with the one-account read again.
/// </remarks>
public class UserListAllowedActionsReadCountTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Handle_WithACaller_ReadsThreeStatements_WhateverThePageSize(int pageSize)
    {
        var db = new RecordingDbConnectionFactory(affectedRows: 0);
        var permissions = new PermissionRepository(db);
        var policy = new AccountActionPolicy(
            permissions, Mock.Of<IUserRepository>(), TimeProvider.System, Mock.Of<ILogger<AccountActionPolicy>>());
        var users = new Mock<IUserRepository>();
        var page = Enumerable.Range(0, pageSize).Select(_ => TestHelpers.CreateUser()).ToList();
        users
            .Setup(r => r.GetPagedAsync(1, pageSize, null, null, It.IsAny<SortDirection>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((page, pageSize));
        var roles = new Mock<IRoleRepository>();
        roles.Setup(r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Role>());

        var result = await new GetUsersQueryHandler(users.Object, roles.Object, permissions, policy, Mock.Of<IImageUrlComposer>())
            .Handle(new GetUsersQuery(1, pageSize) { ActorId = Guid.NewGuid() }, CancellationToken.None);

        result.Value.Users.Should().HaveCount(pageSize).And.OnlyContain(user => user.AllowedActions != null);
        db.Commands.Should().HaveCount(3,
            "the caller's permissions, the page's in one statement, and the holders of *: the same three for 1 row or 100");
        db.Commands.Should().ContainSingle(command => command.Parameters.Keys.Count(key => key.StartsWith("UserIds", StringComparison.Ordinal)) == pageSize,
            "the page's platform permissions are read for every row at once");
    }
}
