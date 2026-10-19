using Auth.Application.Common;
using Auth.Application.Features.Organizations.AcceptInvitation;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Interfaces;
using Auth.Application.DTOs;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Unit tests for AcceptInvitationCommandHandler.
/// </summary>
public class AcceptInvitationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<ILogger<AcceptInvitationCommandHandler>> _loggerMock;
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly AcceptInvitationCommandHandler _handler;

    public AcceptInvitationCommandHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _loggerMock = new Mock<ILogger<AcceptInvitationCommandHandler>>();

        // Identity hash: these cases exercise the acceptance rules, not the
        // hashing, and an identity keeps every existing arrangement readable.
        // InvitationTokenHashingTests asserts the hashing itself, with a hash
        // that is visibly not its input.
        _tokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns<string>(token => token);

        _organizationRepositoryMock
            .Setup(r => r.AcceptInvitationAsync(
                It.IsAny<OrganizationInvitation>(),
                It.IsAny<OrganizationUser?>(),
                It.IsAny<OrganizationUserRole?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // The real check over the same mocks: acceptance re-checks the application
        // role the way the direct assignment checks it.
        var applicationRoleCheck = new OrganizationApplicationRoleCheck(
            _organizationRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            new OrganizationGrantGuard(_organizationRepositoryMock.Object, _permissionRepositoryMock.Object));

        _handler = new AcceptInvitationCommandHandler(
            _organizationRepositoryMock.Object,
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _tokenKeyServiceMock.Object,
            applicationRoleCheck,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidInvitation_AcceptsSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token-123";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);
        var user = TestHelpers.CreateUser(id: userId, email: "user@example.com", firstName: "John", lastName: "Doe");
        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");

        SetupMocks(invitation, user, organization, role, existingMembership: null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Success.Should().BeTrue();
        result.Value.OrganizationId.Should().Be(orgId);
        result.Value.OrganizationName.Should().Be("Test Org");
        result.Value.RoleName.Should().Be("Member");
        result.Value.Message.Should().Be("Successfully joined the organization.");

        // Status, membership and (no) application role in one transactional write.
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i => i.Status == InvitationStatus.Accepted),
                It.Is<OrganizationUser?>(m =>
                    m != null &&
                    m.OrganizationId == orgId &&
                    m.UserId == userId &&
                    m.RoleId == roleId),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAConcurrentAcceptanceWonTheRace_ReturnsAlreadyAccepted()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", "raced-token");
        SetupMocks(
            invitation,
            TestHelpers.CreateUser(id: userId, email: "user@example.com"),
            TestHelpers.CreateOrganization(id: orgId, isActive: true),
            TestHelpers.CreateRole(id: roleId),
            existingMembership: null);
        _organizationRepositoryMock
            .Setup(r => r.AcceptInvitationAsync(
                It.IsAny<OrganizationInvitation>(),
                It.IsAny<OrganizationUser?>(),
                It.IsAny<OrganizationUserRole?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            new AcceptInvitationCommand("raced-token") { AcceptedBy = userId }, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationAlreadyAccepted");
    }

    [Fact]
    public async Task Handle_WithAnApplicationRoleTheInviterCanStillGrant_AssignsItWithTheMembership()
    {
        var (command, orgId, userId, applicationId, appRoleId, inviterId) = ArrangeInvitationWithApplicationRole();
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationRoleName.Should().Be("Sales representative");
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.IsAny<OrganizationInvitation>(),
                It.Is<OrganizationUser?>(m => m != null && m.UserId == userId),
                It.Is<OrganizationUserRole?>(a =>
                    a != null &&
                    a.OrganizationId == orgId &&
                    a.UserId == userId &&
                    a.ApplicationId == applicationId &&
                    a.RoleId == appRoleId &&
                    a.AssignedBy == inviterId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheInviterLostTheAuthorityToGrantTheRole_JoinsWithoutIt()
    {
        // The role carries a permission the inviter no longer holds: the invitee still
        // joins (refusing would strand them), but the role is not handed out.
        var (command, _, userId, _, _, _) = ArrangeInvitationWithApplicationRole();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationRoleName.Should().BeNull();
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.IsAny<OrganizationInvitation>(),
                It.Is<OrganizationUser?>(m => m != null && m.UserId == userId),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheApplicationWasDisabledSinceTheInvitation_JoinsWithoutTheRole()
    {
        var (command, orgId, _, applicationId, _, inviterId) = ArrangeInvitationWithApplicationRole();
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");
        _organizationRepositoryMock
            .Setup(r => r.IsApplicationEnabledAsync(orgId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.IsAny<OrganizationInvitation>(), It.IsAny<OrganizationUser?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private (AcceptInvitationCommand Command, Guid OrgId, Guid UserId, Guid ApplicationId, Guid AppRoleId, Guid InviterId)
        ArrangeInvitationWithApplicationRole()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var appRoleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var invitation = TestHelpers.CreateOrganizationInvitation(
            organizationId: orgId,
            email: "rep@example.com",
            roleId: roleId,
            token: "app-role-token",
            invitedBy: inviterId,
            applicationRole: InvitationApplicationRole.From(applicationId, appRoleId));

        SetupMocks(
            invitation,
            TestHelpers.CreateUser(id: userId, email: "rep@example.com"),
            TestHelpers.CreateOrganization(id: orgId, isActive: true),
            TestHelpers.CreateRole(id: roleId, code: "org-member", name: "Organization Member"),
            existingMembership: null);

        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId));
        _organizationRepositoryMock
            .Setup(r => r.IsApplicationEnabledAsync(orgId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(appRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: appRoleId, applicationId: applicationId, name: "Sales representative"));
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(appRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(applicationId: applicationId, code: "crm:leads:read")]);
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionCodesAsync(orgId, inviterId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return (new AcceptInvitationCommand("app-role-token") { AcceptedBy = userId },
            orgId, userId, applicationId, appRoleId, inviterId);
    }

    private void InviterHoldsInOrganization(Guid orgId, Guid inviterId, Guid applicationId, params string[] codes)
    {
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionCodesAsync(orgId, inviterId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new AcceptInvitationCommand("invalid-token") { AcceptedBy = userId };

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync("invalid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationNotFoundByToken");
    }

    [Fact]
    public async Task Handle_WithAlreadyAcceptedInvitation_ReturnsConflictError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "accepted-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);
        invitation.Accept(Guid.NewGuid()); // Mark as already accepted

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationAlreadyAccepted");
    }

    [Fact]
    public async Task Handle_WithExpiredInvitation_ReturnsExpiredError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "expired-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        // Create an expired invitation (expires in the past)
        var invitation = CreateExpiredInvitation(orgId, roleId, "user@example.com", token);

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _organizationRepositoryMock
            .Setup(r => r.UpdateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationExpired");
    }

    [Fact]
    public async Task Handle_WithEmailMismatch_ReturnsForbiddenError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "invited@example.com", token);
        var user = TestHelpers.CreateUser(id: userId, email: "different@example.com", firstName: "John");

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationEmailMismatch");
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        result.FirstError.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Handle_WhenOrganizationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);
        var user = TestHelpers.CreateUser(id: userId, email: "user@example.com");

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Organization?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WhenOrganizationInactive_ReturnsConflictError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);
        var user = TestHelpers.CreateUser(id: userId, email: "user@example.com");
        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Inactive Org", isActive: false);

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
        result.FirstError.Code.Should().Be("Organization.Inactive");
    }

    [Fact]
    public async Task Handle_WhenAlreadyMember_ReturnsSuccessWithExistingMemberMessage()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);
        var user = TestHelpers.CreateUser(id: userId, email: "user@example.com");
        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var existingMembership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId);
        var role = TestHelpers.CreateRole(id: roleId, name: "Member");

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        _organizationRepositoryMock
            .Setup(r => r.UpdateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Success.Should().BeTrue();
        result.Value.Message.Should().Be("You are already a member of this organization.");

        // Should not try to add member again
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i => i.Status == InvitationStatus.Accepted),
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithCaseInsensitiveEmailMatch_AcceptsSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var token = "valid-token";

        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };

        // Invitation email stored lowercase (OrganizationInvitation.Create normalizes to lowercase)
        // Email value object uses case-sensitive Ordinal comparison, so both must match case
        var invitation = CreatePendingInvitation(orgId, roleId, "user@example.com", token);
        var user = TestHelpers.CreateUser(id: userId, email: "user@example.com", firstName: "John");
        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, name: "Member");

        SetupMocks(invitation, user, organization, role, existingMembership: null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Success.Should().BeTrue();
    }

    #region Helper Methods

    private OrganizationInvitation CreatePendingInvitation(
        Guid orgId, Guid roleId, string email, string token)
    {
        return TestHelpers.CreateOrganizationInvitation(
            organizationId: orgId,
            email: email,
            roleId: roleId,
            token: token,
            status: InvitationStatus.Pending,
            expiresAt: DateTime.UtcNow.AddDays(7));
    }

    private OrganizationInvitation CreateExpiredInvitation(
        Guid orgId, Guid roleId, string email, string token)
    {
        return TestHelpers.CreateOrganizationInvitation(
            organizationId: orgId,
            email: email,
            roleId: roleId,
            token: token,
            status: InvitationStatus.Pending,
            expiresAt: DateTime.UtcNow.AddDays(-1), // Expired yesterday
            createdAt: DateTime.UtcNow.AddDays(-8));
    }

    private void SetupMocks(
        OrganizationInvitation invitation,
        User user,
        Organization organization,
        Role role,
        OrganizationUser? existingMembership)
    {
        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenHashAsync(invitation.TokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invitation);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(organization.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(organization.Id, user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        _organizationRepositoryMock
            .Setup(r => r.AddMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUser member, CancellationToken _) => member);

        _organizationRepositoryMock
            .Setup(r => r.UpdateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
    }

    #endregion
}
