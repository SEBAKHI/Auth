using Auth_API.Modules.OrganizationManagement.Queries;
using Auth_API.Tests.Helpers;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using ErrorOr;

namespace Auth_API.Tests.OrganizationManagement.Queries;

/// <summary>
/// Unit tests for GetOrganizationByIdQueryHandler.
/// </summary>
public class GetOrganizationByIdQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly GetOrganizationByIdQueryHandler _handler;

    public GetOrganizationByIdQueryHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();

        _handler = new GetOrganizationByIdQueryHandler(
            _organizationRepositoryMock.Object,
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _applicationRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidMember_ReturnsOrganizationDetails()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        var query = new GetOrganizationByIdQuery(orgId) { RequestedBy = requesterId };

        var organization = TestHelpers.CreateOrganization(
            id: orgId,
            code: "test-org",
            name: "Test Organization",
            description: "A test org",
            contactEmail: "test@org.com",
            ownerId: ownerId,
            isActive: true);

        var owner = TestHelpers.CreateUser(id: ownerId, email: "owner@org.com", firstName: "John", lastName: "Owner");
        var requester = TestHelpers.CreateUser(id: requesterId, email: "requester@org.com", firstName: "Jane", lastName: "Member");
        var membership = TestHelpers.CreateOrganizationUser(
            organizationId: orgId,
            userId: requesterId,
            roleId: roleId,
            invitedBy: ownerId);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var enabledApp = TestHelpers.CreateOrganizationApplication(
            organizationId: orgId,
            applicationId: appId,
            isActive: true,
            enabledBy: ownerId,
            subscriptionTier: "pro");
        var application = TestHelpers.CreateApplication(id: appId, code: "TEST-APP", name: "Test App", description: "A test app");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, requesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);

        _organizationRepositoryMock
            .Setup(r => r.GetMembersAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationUser> { membership });

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(requesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requester);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _organizationRepositoryMock
            .Setup(r => r.GetEnabledApplicationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationApplication> { enabledApp });

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(orgId);
        result.Value.Code.Should().Be("test-org");
        result.Value.Name.Should().Be("Test Organization");
        result.Value.OwnerId.Should().Be(ownerId);
        result.Value.OwnerName.Should().Be("John Owner");
        result.Value.OwnerEmail.Should().Be("owner@org.com");
        result.Value.MemberCount.Should().Be(1);
        result.Value.EnabledAppCount.Should().Be(1);
        result.Value.Members.Should().HaveCount(1);
        result.Value.EnabledApplications.Should().HaveCount(1);
        result.Value.EnabledApplications.First().SubscriptionTier.Should().Be("pro");
    }

    [Fact]
    public async Task Handle_WhenOrganizationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var query = new GetOrganizationByIdQuery(orgId) { RequestedBy = requesterId };

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WhenUserNotMember_ReturnsNotFoundError()
    {
        // Arrange - Security: returns NotFound to not reveal org existence to non-members
        var orgId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var query = new GetOrganizationByIdQuery(orgId) { RequestedBy = requesterId };

        var organization = TestHelpers.CreateOrganization(
            id: orgId,
            code: "test-org",
            name: "Test Organization",
            isActive: true);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert - Returns NotFound to hide org existence from non-members
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("Organization.NotMember");
    }

    [Fact]
    public async Task Handle_WithMultipleMembersAndApps_ReturnsCompleteData()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var member1Id = Guid.NewGuid();
        var member2Id = Guid.NewGuid();
        var role1Id = Guid.NewGuid();
        var role2Id = Guid.NewGuid();
        var app1Id = Guid.NewGuid();
        var app2Id = Guid.NewGuid();

        var query = new GetOrganizationByIdQuery(orgId) { RequestedBy = member1Id };

        var organization = TestHelpers.CreateOrganization(
            id: orgId,
            code: "multi-org",
            name: "Multi Organization",
            ownerId: ownerId,
            isActive: true);

        var membership1 = TestHelpers.CreateOrganizationUser(
            organizationId: orgId,
            userId: member1Id,
            roleId: role1Id,
            invitedBy: ownerId);

        var membership2 = TestHelpers.CreateOrganizationUser(
            organizationId: orgId,
            userId: member2Id,
            roleId: role2Id,
            invitedBy: ownerId);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, member1Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership1);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(firstName: "Test", lastName: "User", email: "test@test.com"));

        _organizationRepositoryMock
            .Setup(r => r.GetMembersAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationUser> { membership1, membership2 });

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(code: "MEMBER", name: "Member"));

        var apps = new List<OrganizationApplication>
        {
            TestHelpers.CreateOrganizationApplication(organizationId: orgId, applicationId: app1Id, isActive: true, enabledBy: ownerId),
            TestHelpers.CreateOrganizationApplication(organizationId: orgId, applicationId: app2Id, isActive: true, enabledBy: ownerId)
        };

        _organizationRepositoryMock
            .Setup(r => r.GetEnabledApplicationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(apps);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(code: "APP", name: "App"));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.MemberCount.Should().Be(2);
        result.Value.EnabledAppCount.Should().Be(2);
        result.Value.Members.Should().HaveCount(2);
        result.Value.EnabledApplications.Should().HaveCount(2);
    }
}
