using Auth.Application.Features.Roles.GetRoleApplications;
using Auth.Application.Features.Roles.GetRoleUsers;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Access;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.RoleManagement.Queries;

/// <summary>
/// Unit tests for GetRoleUsersQueryHandler.
/// </summary>
public class GetRoleUsersQueryHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly GetRoleUsersQueryHandler _handler;

    public GetRoleUsersQueryHandlerTests()
    {
        _handler = new GetRoleUsersQueryHandler(
            _roleRepositoryMock.Object,
            new Mock<ILogger<GetRoleUsersQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidRole_ReturnsPagedUsersWithAssignmentSource()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(id: roleId);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _roleRepositoryMock
            .Setup(r => r.GetUsersPagedAsync(roleId, 1, 20, null, null, SortDirection.Asc, It.IsAny<CancellationToken>()))
            .ReturnsAsync((
            [
                new RoleUserRow
                {
                    UserId = Guid.NewGuid(), Email = "direct@test.com", FirstName = "Dana", LastName = "Direct",
                    Status = UserStatus.Active, CreatedAt = DateTime.UtcNow, ViaDirect = true, ViaOrganization = false
                },
                new RoleUserRow
                {
                    UserId = Guid.NewGuid(), Email = "org@test.com", FirstName = "Oscar", LastName = "Org",
                    Status = UserStatus.Active, CreatedAt = DateTime.UtcNow, ViaDirect = false, ViaOrganization = true,
                    OrganizationNames = "Acme Corp"
                },
                new RoleUserRow
                {
                    UserId = Guid.NewGuid(), Email = "both@test.com", FirstName = "Billie", LastName = "Both",
                    Status = UserStatus.Active, CreatedAt = DateTime.UtcNow, ViaDirect = true, ViaOrganization = true,
                    OrganizationNames = "Acme Corp"
                }
            ], 3));

        // Act
        var result = await _handler.Handle(new GetRoleUsersQuery(roleId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Users.Single(u => u.Email == "direct@test.com").AssignmentSource.Should().Be("direct");
        result.Value.Users.Single(u => u.Email == "org@test.com").AssignmentSource.Should().Be("organization");
        result.Value.Users.Single(u => u.Email == "both@test.com").AssignmentSource.Should().Be("both");
        result.Value.Users.Single(u => u.Email == "org@test.com").OrganizationNames.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(new GetRoleUsersQuery(roleId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

/// <summary>
/// Unit tests for GetRoleApplicationsQueryHandler.
/// </summary>
public class GetRoleApplicationsQueryHandlerTests
{
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly GetRoleApplicationsQueryHandler _handler;

    public GetRoleApplicationsQueryHandlerTests()
    {
        _handler = new GetRoleApplicationsQueryHandler(
            _roleRepositoryMock.Object,
            new Mock<ILogger<GetRoleApplicationsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidRole_ReturnsApplicationsWithRelationship()
    {
        // Arrange
        var roleId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(id: roleId, applicationId: Guid.NewGuid());

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _roleRepositoryMock
            .Setup(r => r.GetRoleApplicationsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RoleApplicationRow(Guid.NewGuid(), "CMS", "CMS App", null, true, IsOwner: true, IsAssigned: true),
                new RoleApplicationRow(Guid.NewGuid(), "CRM", "CRM App", null, true, IsOwner: false, IsAssigned: true)
            ]);

        // Act
        var result = await _handler.Handle(new GetRoleApplicationsQuery(roleId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(2);
        result.Value.Single(a => a.Code == "CMS").Relationship.Should().Be("both");
        result.Value.Single(a => a.Code == "CRM").Relationship.Should().Be("assigned");
    }

    [Fact]
    public async Task Handle_RoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var roleId = Guid.NewGuid();

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(new GetRoleApplicationsQuery(roleId), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _roleRepositoryMock.Verify(
            r => r.GetRoleApplicationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
