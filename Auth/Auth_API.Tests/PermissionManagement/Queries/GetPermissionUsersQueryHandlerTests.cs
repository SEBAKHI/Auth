using Auth.Application.Features.Permissions.GetPermissionUsers;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.PermissionManagement.Queries;

/// <summary>
/// Unit tests for GetPermissionUsersQueryHandler.
/// </summary>
public class GetPermissionUsersQueryHandlerTests
{
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly GetPermissionUsersQueryHandler _handler;

    public GetPermissionUsersQueryHandlerTests()
    {
        _handler = new GetPermissionUsersQueryHandler(
            _permissionRepositoryMock.Object,
            Mock.Of<IImageUrlComposer>(),
            new Mock<ILogger<GetPermissionUsersQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidPermission_ReturnsPagedUsersWithGrantSources()
    {
        // Arrange
        var permissionId = Guid.NewGuid();
        var permission = TestHelpers.CreatePermission(id: permissionId);
        var row = new PermissionUserRow
        {
            UserId = Guid.NewGuid(),
            Email = "granted@test.com",
            FirstName = "Gina",
            LastName = "Granted",
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow,
            ViaDirect = true,
            ViaOrganization = false,
            ViaRole = true,
            RoleNames = "Administrator"
        };

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permission);

        _permissionRepositoryMock
            .Setup(r => r.GetUsersPagedAsync(permissionId, 1, 20, null, null, SortDirection.Asc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([row], 1));

        // Act
        var result = await _handler.Handle(new GetPermissionUsersQuery(permissionId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.TotalCount.Should().Be(1);
        var dto = result.Value.Users.Single();
        dto.Email.Should().Be("granted@test.com");
        dto.FullName.Should().Be("Gina Granted");
        dto.ViaDirect.Should().BeTrue();
        dto.ViaOrganization.Should().BeFalse();
        dto.ViaRole.Should().BeTrue();
        dto.RoleNames.Should().Be("Administrator");
    }

    [Fact]
    public async Task Handle_PassesPagingAndSortingThrough()
    {
        // Arrange
        var permissionId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePermission(id: permissionId));

        _permissionRepositoryMock
            .Setup(r => r.GetUsersPagedAsync(permissionId, 3, 50, "smith", "lastName", SortDirection.Desc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0));

        // Act
        var result = await _handler.Handle(
            new GetPermissionUsersQuery(permissionId, PageNumber: 3, PageSize: 50, SearchTerm: "smith", SortBy: "lastName", SortDirection: SortDirection.Desc),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _permissionRepositoryMock.Verify(
            r => r.GetUsersPagedAsync(permissionId, 3, 50, "smith", "lastName", SortDirection.Desc, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PermissionNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var permissionId = Guid.NewGuid();

        _permissionRepositoryMock
            .Setup(r => r.GetByIdAsync(permissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Permission?)null);

        // Act
        var result = await _handler.Handle(new GetPermissionUsersQuery(permissionId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _permissionRepositoryMock.Verify(
            r => r.GetUsersPagedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
