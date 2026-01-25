using Auth_API.Modules.OrganizationManagement.Commands;
using Auth_API.Tests.Helpers;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
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
    private readonly AcceptInvitationCommandHandler _handler;

    public AcceptInvitationCommandHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _loggerMock = new Mock<ILogger<AcceptInvitationCommandHandler>>();

        _handler = new AcceptInvitationCommandHandler(
            _organizationRepositoryMock.Object,
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
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

        _organizationRepositoryMock.Verify(
            r => r.AddMemberAsync(It.Is<OrganizationUser>(m =>
                m.OrganizationId == orgId &&
                m.UserId == userId &&
                m.RoleId == roleId),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _organizationRepositoryMock.Verify(
            r => r.UpdateInvitationAsync(It.Is<OrganizationInvitation>(i =>
                i.Status == InvitationStatus.Accepted),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithInvalidToken_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new AcceptInvitationCommand("invalid-token") { AcceptedBy = userId };

        _organizationRepositoryMock
            .Setup(r => r.GetInvitationByTokenAsync("invalid-token", It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenAsync(token, It.IsAny<CancellationToken>()))
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
            r => r.AddMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        // Invitation email in different case than user email
        var invitation = CreatePendingInvitation(orgId, roleId, "USER@EXAMPLE.COM", token);
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
            .Setup(r => r.GetInvitationByTokenAsync(invitation.Token, It.IsAny<CancellationToken>()))
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
