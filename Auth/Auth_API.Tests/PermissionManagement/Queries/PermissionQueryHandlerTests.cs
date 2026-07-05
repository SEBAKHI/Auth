using Auth.Application.Features.Permissions.GetPermissionById;
using Auth.Application.Features.Permissions.GetPermissions;
using Auth.Application.Features.Permissions.GetPermissionImplications;
using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.PermissionManagement.Queries;

/// <summary>
/// Unit tests for GetPermissionByIdQueryHandler.
/// </summary>
public class GetPermissionByIdQueryHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<ILogger<GetPermissionByIdQueryHandler>> _loggerMock;
    private readonly GetPermissionByIdQueryHandler _handler;

    public GetPermissionByIdQueryHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _loggerMock = new Mock<ILogger<GetPermissionByIdQueryHandler>>();

        _handler = new GetPermissionByIdQueryHandler(
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidId_ReturnsPermissionDto()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var query = new GetPermissionByIdQuery(Id: permissionId);

        var permission = TestHelpers.CreatePermission(
            id: permissionId,
            applicationId: applicationId,
            code: "orders:read",
            name: "Read Orders",
            description: "Allows reading order data");

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, name: "Orders App"));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(permissionId);
        result.Value.ApplicationId.Should().Be(applicationId);
        result.Value.ApplicationName.Should().Be("Orders App");
        result.Value.Code.Should().Be(permission.Code);
        result.Value.Name.Should().Be(permission.Name);
        result.Value.Description.Should().Be(permission.Description);
        result.Value.IsActive.Should().Be(permission.IsActive);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var query = new GetPermissionByIdQuery(Id: permissionId);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Permission.NotFound");
    }
}

/// <summary>
/// Unit tests for GetPermissionsQueryHandler.
/// </summary>
public class GetPermissionsQueryHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly GetPermissionsQueryHandler _handler;

    public GetPermissionsQueryHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();

        _handler = new GetPermissionsQueryHandler(
            _permissionRepositoryMock.Object,
            _applicationRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithApplicationId_ReturnsFilteredPermissions()
    {
        // Arrange
        var applicationId = Guid.NewGuid();
        var query = new GetPermissionsQuery(ApplicationId: applicationId);

        var permissions = new List<Permission>
        {
            TestHelpers.CreatePermission(applicationId: applicationId, code: "orders:read", name: "Read Orders"),
            TestHelpers.CreatePermission(applicationId: applicationId, code: "orders:write", name: "Write Orders")
        };

        _permissionRepositoryMock
            .Setup(r => r.GetByApplicationAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, name: "Orders App"));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(p => p.ApplicationId.Should().Be(applicationId));
        result.Value.Should().OnlyContain(p => p.ApplicationName == "Orders App");

        // Distinct application ids resolve once, not per permission.
        _applicationRepositoryMock.Verify(
            r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()),
            Times.Once);

        _permissionRepositoryMock.Verify(
            r => r.GetByApplicationAsync(applicationId, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _permissionRepositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoApplicationId_ReturnsAllPermissions()
    {
        // Arrange
        var query = new GetPermissionsQuery(ApplicationId: null);

        var permissions = new List<Permission>
        {
            TestHelpers.CreatePermission(code: "orders:read", name: "Read Orders"),
            TestHelpers.CreatePermission(code: "users:read", name: "Read Users"),
            TestHelpers.CreatePermission(code: "admin:manage", name: "Admin Manage")
        };

        _permissionRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissions);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(3);

        _permissionRepositoryMock.Verify(
            r => r.GetAllAsync(It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _permissionRepositoryMock.Verify(
            r => r.GetByApplicationAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Unit tests for GetPermissionImplicationsQueryHandler.
/// </summary>
public class GetPermissionImplicationsQueryHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<ILogger<GetPermissionImplicationsQueryHandler>> _loggerMock;
    private readonly GetPermissionImplicationsQueryHandler _handler;

    public GetPermissionImplicationsQueryHandlerTests()
    {
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _loggerMock = new Mock<ILogger<GetPermissionImplicationsQueryHandler>>();

        _handler = new GetPermissionImplicationsQueryHandler(
            _permissionRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidPermission_ReturnsImpliedPermissions()
    {
        // Arrange
        var sourcePermissionId = Guid.NewGuid();
        var impliedPermission1Id = Guid.NewGuid();
        var impliedPermission2Id = Guid.NewGuid();
        var query = new GetPermissionImplicationsQuery(PermissionId: sourcePermissionId);

        var sourcePermission = TestHelpers.CreatePermission(
            id: sourcePermissionId,
            code: "orders:manage");

        var impliedPermission1 = TestHelpers.CreatePermission(
            id: impliedPermission1Id,
            code: "orders:read",
            name: "Read Orders");

        var impliedPermission2 = TestHelpers.CreatePermission(
            id: impliedPermission2Id,
            code: "orders:write",
            name: "Write Orders");

        var implications = new List<PermissionImplication>
        {
            TestHelpers.CreatePermissionImplication(
                permissionId: sourcePermissionId,
                impliedPermissionId: impliedPermission1Id),
            TestHelpers.CreatePermissionImplication(
                permissionId: sourcePermissionId,
                impliedPermissionId: impliedPermission2Id)
        };

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(sourcePermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourcePermission);

        _permissionRepositoryMock
            .Setup(r => r.GetImplicationsAsync(sourcePermissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(implications);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(impliedPermission1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impliedPermission1);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(impliedPermission2Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impliedPermission2);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(p => p.Id == impliedPermission1Id);
        result.Value.Should().Contain(p => p.Id == impliedPermission2Id);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var query = new GetPermissionImplicationsQuery(PermissionId: permissionId);

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Permission.NotFound");

        _permissionRepositoryMock.Verify(
            r => r.GetImplicationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
