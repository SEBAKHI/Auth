using Auth.Application.Features.Users.GetUserById;
using Auth.Application.Features.Users.GetUsers;
using Auth.Application.Features.Users.GetUserRoles;
using Auth.Application.Features.Users.GetUserPermissions;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Queries;

public class GetUserByIdQueryHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<ITwoFactorStateStore> _twoFactorStateStoreMock = new();
    private readonly Mock<IPlatformMfaPolicy> _platformMfaPolicyMock = new();
    private readonly GetUserByIdQueryHandler _handler;

    public GetUserByIdQueryHandlerTests()
    {
        _handler = new GetUserByIdQueryHandler(
            _userRepositoryMock.Object, _roleRepositoryMock.Object, _permissionRepositoryMock.Object,
            AccountRules.Policy(), Mock.Of<IImageUrlComposer>(), _twoFactorStateStoreMock.Object,
            TestHelpers.CreateOptions(new Auth.Application.Configuration.EmailSettings()), _platformMfaPolicyMock.Object);
    }

    // Review of #107 (F4): the screens learn from the server that the policy keeps the
    // app, instead of offering a switch-off or the emailed code the server refuses.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_SaysWhetherAPolicyRequiresTheAccountsApp(bool enforced)
    {
        var user = TestHelpers.CreateUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _roleRepositoryMock.Setup(r => r.GetUserRolesAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Role>());
        _permissionRepositoryMock.Setup(r => r.GetUserEffectivePermissionsAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        _platformMfaPolicyMock.Setup(p => p.IsEnforcedForUserAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(enforced);

        var result = await _handler.Handle(new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.Value.TwoFactorRequiredByPolicy.Should().Be(enforced);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsUserDto()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _roleRepositoryMock.Setup(r => r.GetUserRolesAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Role>());
        _permissionRepositoryMock.Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());

        var result = await _handler.Handle(new GetUserByIdQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Id.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new GetUserByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class GetUsersQueryHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly GetUsersQueryHandler _handler;

    public GetUsersQueryHandlerTests()
    {
        _handler = new GetUsersQueryHandler(
            _userRepositoryMock.Object, _roleRepositoryMock.Object, _permissionRepositoryMock.Object,
            AccountRules.Policy(), Mock.Of<IImageUrlComposer>());
    }

    [Fact]
    public async Task Handle_ValidQuery_ReturnsPagedResults()
    {
        var user = TestHelpers.CreateUser();
        _userRepositoryMock
            .Setup(r => r.GetPagedAsync(1, 20, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<User> { user } as IReadOnlyList<User>, 1));
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => (IReadOnlyList<Role>)[]));
        _permissionRepositoryMock
            .Setup(r => r.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => (IReadOnlyList<string>)[]));

        var result = await _handler.Handle(new GetUsersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Users.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_IncludeDeleted_PassesFlagThroughAndMapsDeletionState()
    {
        var deletedAt = DateTime.UtcNow.AddDays(-1);
        var user = TestHelpers.CreateUser(isDeleted: true, deletedAt: deletedAt);
        _userRepositoryMock
            .Setup(r => r.GetPagedAsync(1, 20, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<User> { user } as IReadOnlyList<User>, 1));
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => (IReadOnlyList<Role>)[]));
        _permissionRepositoryMock
            .Setup(r => r.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => (IReadOnlyList<string>)[]));

        var result = await _handler.Handle(
            new GetUsersQuery(IncludeDeleted: true), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Users.Should().HaveCount(1);
        result.Value.Users[0].IsDeleted.Should().BeTrue();
        result.Value.Users[0].DeletedAt.Should().Be(deletedAt);
    }

    /// <summary>
    /// OI-225 (6): the page's roles come from one batched read, each row keeps its own,
    /// and the one-account read is never called. Deliberate break: read each row's
    /// roles with GetUserRolesAsync in the loop again.
    /// </summary>
    [Fact]
    public async Task Handle_ReadsThePagesRolesOnce_AndMapsEachRowsOwn()
    {
        var first = TestHelpers.CreateUser();
        var second = TestHelpers.CreateUser();
        var third = TestHelpers.CreateUser();
        _userRepositoryMock
            .Setup(r => r.GetPagedAsync(1, 20, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<User> { first, second, third } as IReadOnlyList<User>, 3));
        var pageRoles = new Dictionary<Guid, IReadOnlyList<Role>>
        {
            [first.Id] = [TestHelpers.CreateRole(code: "auditor"), TestHelpers.CreateRole(code: "support")],
            [second.Id] = [],
            [third.Id] = [TestHelpers.CreateRole(code: "user-manager")],
        };
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesForUsersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pageRoles);
        _permissionRepositoryMock
            .Setup(r => r.GetUsersEffectivePermissionsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, _ => (IReadOnlyList<string>)[]));

        var result = await _handler.Handle(new GetUsersQuery(), CancellationToken.None);

        result.Value.Users.Select(u => u.Roles).Should().BeEquivalentTo(
            new[] { new[] { "auditor", "support" }, Array.Empty<string>(), new[] { "user-manager" } },
            options => options.WithStrictOrdering());
        _roleRepositoryMock.Verify(
            r => r.GetUserRolesForUsersAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { first.Id, second.Id, third.Id })),
                It.IsAny<CancellationToken>()),
            Times.Once());
        _roleRepositoryMock.Verify(
            r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}

public class GetUserRolesQueryHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly GetUserRolesQueryHandler _handler;

    public GetUserRolesQueryHandlerTests()
    {
        _handler = new GetUserRolesQueryHandler(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            AccountRules.Policy(),
            new Mock<ILogger<GetUserRolesQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUserId_ReturnsRoles()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var userRole = TestHelpers.CreateUserRole(userId: userId, roleId: roleId);
        var role = TestHelpers.CreateRole(id: roleId, name: "Admin", code: "ADMIN");

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _userRepositoryMock.Setup(r => r.GetUserRolesAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserRole> { userRole });
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);

        var result = await _handler.Handle(new GetUserRolesQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new GetUserRolesQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class GetUserPermissionsQueryHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly GetUserPermissionsQueryHandler _handler;

    public GetUserPermissionsQueryHandlerTests()
    {
        _handler = new GetUserPermissionsQueryHandler(
            _userRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            AccountRules.Policy(),
            new Mock<ILogger<GetUserPermissionsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUserId_ReturnsPermissions()
    {
        var userId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var userPerm = TestHelpers.CreateUserPermission(userId: userId, permissionId: permId);
        var permission = TestHelpers.CreatePermission(id: permId, name: "Read", code: "app:read");

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _userRepositoryMock.Setup(r => r.GetUserPermissionsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserPermission> { userPerm });
        _permissionRepositoryMock.Setup(r => r.GetByIdAsync(permId, It.IsAny<CancellationToken>())).ReturnsAsync(permission);

        var result = await _handler.Handle(new GetUserPermissionsQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new GetUserPermissionsQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
