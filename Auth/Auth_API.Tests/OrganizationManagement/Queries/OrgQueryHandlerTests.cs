using Auth.Application.Features.Organizations.GetUserOrganizations;
using Auth.Application.Features.Organizations.GetOrganizationMembers;
using Auth.Application.Features.Organizations.GetPendingInvitations;
using Auth.Application.Features.Organizations.GetOrganizationApplications;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Queries;

public class GetUserOrganizationsQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly GetUserOrganizationsQueryHandler _handler;

    public GetUserOrganizationsQueryHandlerTests()
    {
        _handler = new GetUserOrganizationsQueryHandler(
            _orgRepoMock.Object,
            _roleRepoMock.Object);
    }

    [Fact]
    public async Task Handle_ValidUserId_ReturnsOrganizations()
    {
        var userId = Guid.NewGuid();
        var memberships = new List<OrganizationUser>
        {
            TestHelpers.CreateOrganizationUser(userId: userId)
        };
        var org = TestHelpers.CreateOrganization(id: memberships[0].OrganizationId, isActive: true);

        _orgRepoMock.Setup(r => r.GetUserMembershipsAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(memberships);
        _orgRepoMock.Setup(r => r.GetByIdAsync(memberships[0].OrganizationId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _roleRepoMock.Setup(r => r.GetByIdAsync(memberships[0].RoleId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateRole(id: memberships[0].RoleId));
        _orgRepoMock.Setup(r => r.GetMembersAsync(memberships[0].OrganizationId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationUser> { memberships[0] });

        var result = await _handler.Handle(new GetUserOrganizationsQuery(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
    }
}

public class GetOrganizationMembersQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly GetOrganizationMembersQueryHandler _handler;

    public GetOrganizationMembersQueryHandlerTests()
    {
        _handler = new GetOrganizationMembersQueryHandler(
            _orgRepoMock.Object,
            _userRepoMock.Object,
            _roleRepoMock.Object);
    }

    [Fact]
    public async Task Handle_ValidOrgId_ReturnsMembers()
    {
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var members = new List<OrganizationUser>
        {
            TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId, roleId: roleId)
        };
        var requesterMembership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: requestedBy);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, requestedBy, It.IsAny<CancellationToken>())).ReturnsAsync(requesterMembership);
        _orgRepoMock.Setup(r => r.GetMembersPagedAsync(orgId, 1, 20, null, It.IsAny<string?>(), It.IsAny<SortDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((members as IReadOnlyList<OrganizationUser>, 1));
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _userRepoMock.Setup(r => r.GetByIdAsync(members[0].InvitedBy, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: members[0].InvitedBy));
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateRole(id: roleId));

        var result = await _handler.Handle(
            new GetOrganizationMembersQuery(orgId) { RequestedBy = requestedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_OrgNotFound_ReturnsError()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        var result = await _handler.Handle(
            new GetOrganizationMembersQuery(Guid.NewGuid()) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class GetPendingInvitationsQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly GetPendingInvitationsQueryHandler _handler;

    public GetPendingInvitationsQueryHandlerTests()
    {
        _handler = new GetPendingInvitationsQueryHandler(
            _orgRepoMock.Object,
            _userRepoMock.Object,
            _roleRepoMock.Object);
    }

    [Fact]
    public async Task Handle_ValidOrgId_ReturnsInvitations()
    {
        var orgId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var requesterMembership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: requestedBy);
        var invitations = new List<OrganizationInvitation>
        {
            TestHelpers.CreateOrganizationInvitation(organizationId: orgId)
        };

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, requestedBy, It.IsAny<CancellationToken>())).ReturnsAsync(requesterMembership);
        _orgRepoMock.Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(invitations);
        _roleRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateRole());
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser());

        var result = await _handler.Handle(
            new GetPendingInvitationsQuery(orgId) { RequestedBy = requestedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_OrgNotFound_ReturnsError()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        var result = await _handler.Handle(
            new GetPendingInvitationsQuery(Guid.NewGuid()) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class GetOrganizationApplicationsQueryHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly GetOrganizationApplicationsQueryHandler _handler;

    public GetOrganizationApplicationsQueryHandlerTests()
    {
        _handler = new GetOrganizationApplicationsQueryHandler(
            _orgRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<GetOrganizationApplicationsQueryHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidOrgId_ReturnsApplications()
    {
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var orgApps = new List<OrganizationApplication>
        {
            TestHelpers.CreateOrganizationApplication(organizationId: orgId, applicationId: appId)
        };
        var app = TestHelpers.CreateApplication(id: appId);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.IsMemberAsync(orgId, requestedBy, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _orgRepoMock.Setup(r => r.GetEnabledApplicationsAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(orgApps);
        _orgRepoMock.Setup(r => r.GetAssignedUserCountsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [appId] = 2 });
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(app);

        var result = await _handler.Handle(
            new GetOrganizationApplicationsQuery(orgId) { RequestedBy = requestedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Should().HaveCount(1);
        result.Value[0].AssignedUserCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OrgNotFound_ReturnsError()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        var result = await _handler.Handle(
            new GetOrganizationApplicationsQuery(Guid.NewGuid()) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

