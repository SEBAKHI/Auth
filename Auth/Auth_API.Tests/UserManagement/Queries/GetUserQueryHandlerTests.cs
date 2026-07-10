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
    private readonly GetUserByIdQueryHandler _handler;

    public GetUserByIdQueryHandlerTests()
    {
        _handler = new GetUserByIdQueryHandler(
            _userRepositoryMock.Object, _roleRepositoryMock.Object, _permissionRepositoryMock.Object,
            Mock.Of<IImageUrlComposer>());
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
            Mock.Of<IImageUrlComposer>());
    }

    [Fact]
    public async Task Handle_ValidQuery_ReturnsPagedResults()
    {
        var user = TestHelpers.CreateUser();
        _userRepositoryMock
            .Setup(r => r.GetPagedAsync(1, 20, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<User> { user } as IReadOnlyList<User>, 1));
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>());
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var result = await _handler.Handle(new GetUsersQuery(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Users.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(1);
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
