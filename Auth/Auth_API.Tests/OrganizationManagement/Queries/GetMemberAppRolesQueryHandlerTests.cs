using Auth.Application.Features.Organizations.GetMemberAppRoles;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Queries;

/// <summary>
/// Unit tests for GetMemberAppRolesQueryHandler.
/// </summary>
public class GetMemberAppRolesQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILogger<GetMemberAppRolesQueryHandler>> _loggerMock;
    private readonly GetMemberAppRolesQueryHandler _handler;

    public GetMemberAppRolesQueryHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loggerMock = new Mock<ILogger<GetMemberAppRolesQueryHandler>>();

        _handler = new GetMemberAppRolesQueryHandler(
            _organizationRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _userRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenOrganizationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var query = new GetMemberAppRolesQuery(Guid.NewGuid(), Guid.NewGuid())
        {
            RequestedBy = Guid.NewGuid()
        };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Organization.NotFound");
    }

    [Fact]
    public async Task Handle_WhenUserNotMember_ReturnsNotMemberError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetMemberAppRolesQuery(orgId, userId) { RequestedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.NotMember");
    }

    [Fact]
    public async Task Handle_WithNoAssignments_ReturnsEmptyList()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = new GetMemberAppRolesQuery(orgId, userId) { RequestedBy = Guid.NewGuid() };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));

        _organizationRepositoryMock
            .Setup(r => r.GetUserAppRolesAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationUserRole>());

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAssignments_ReturnsEnrichedDtos()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignedById = Guid.NewGuid();
        var expiresAt = DateTime.UtcNow.AddDays(30);

        var query = new GetMemberAppRolesQuery(orgId, userId) { RequestedBy = Guid.NewGuid() };

        var assignment = OrganizationUserRole.Create(orgId, userId, appId, roleId, assignedById, expiresAt);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));

        _organizationRepositoryMock
            .Setup(r => r.GetUserAppRolesAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationUserRole> { assignment });

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId, code: "CMS", name: "CMS"));

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: appId, code: "CMS-EDITOR", name: "Editor"));

        _userRepositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<User> { TestHelpers.CreateUser(id: assignedById, firstName: "Admin", lastName: "User") });

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
        var dto = result.Value[0];
        dto.ApplicationId.Should().Be(appId);
        dto.ApplicationCode.Should().Be("CMS");
        dto.ApplicationName.Should().Be("CMS");
        dto.RoleId.Should().Be(roleId);
        dto.RoleCode.Should().Be("CMS-EDITOR");
        dto.RoleName.Should().Be("Editor");
        dto.AssignedBy.Should().Be(assignedById);
        dto.AssignedByName.Should().Be("Admin User");
        dto.ExpiresAt.Should().Be(expiresAt);
    }
}
