using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.InviteMember;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth_API.Tests.Helpers;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

/// <summary>
/// Unit tests for InviteMemberCommandHandler.
/// </summary>
public class InviteMemberCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<ISecureTokenGenerator> _tokenGeneratorMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<ILogger<InviteMemberCommandHandler>> _loggerMock;
    private readonly InviteMemberCommandHandler _handler;

    public InviteMemberCommandHandlerTests()
    {
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _tokenGeneratorMock = new Mock<ISecureTokenGenerator>();
        _notificationServiceMock = new Mock<INotificationService>();
        _loggerMock = new Mock<ILogger<InviteMemberCommandHandler>>();

        _tokenGeneratorMock
            .Setup(g => g.Generate())
            .Returns("dGVzdC10b2tlbi1mb3ItaW52aXRhdGlvbi10aGF0LWlzLWxvbmctZW5vdWdo");

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        _handler = new InviteMemberCommandHandler(
            _organizationRepositoryMock.Object,
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _tokenGeneratorMock.Object,
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(new EmailSettings { FrontendBaseUrl = "https://accounts.example.com" }),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithValidData_CreatesInvitationSuccessfully()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "newmember@example.com",
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com", firstName: "John", lastName: "Doe");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>());

        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation inv, CancellationToken _) => inv);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().NotBeNull();
        result.Value.Email.Should().Be(command.Email.ToLowerInvariant());
        result.Value.OrganizationId.Should().Be(orgId);
        result.Value.OrganizationName.Should().Be("Test Org");
        result.Value.RoleId.Should().Be(roleId);
        result.Value.RoleName.Should().Be("Member");
        result.Value.Status.Should().Be("Pending");
        result.Value.InvitedBy.Should().Be(inviterId);
        result.Value.InvitedByName.Should().Be("John Doe");

        _organizationRepositoryMock.Verify(
            r => r.CreateInvitationAsync(It.Is<OrganizationInvitation>(i =>
                i.Email == command.Email.ToLowerInvariant() &&
                i.OrganizationId == orgId &&
                i.RoleId == roleId &&
                i.InvitedBy == inviterId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOrganizationNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "test@example.com",
            RoleId: Guid.NewGuid())
        { InvitedBy = Guid.NewGuid() };

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
        var orgId = Guid.NewGuid();
        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "test@example.com",
            RoleId: Guid.NewGuid())
        { InvitedBy = Guid.NewGuid() };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Inactive Org", isActive: false);

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
    public async Task Handle_WhenRoleNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "test@example.com",
            RoleId: roleId)
        { InvitedBy = Guid.NewGuid() };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Contain("Role");
    }

    [Fact]
    public async Task Handle_WhenInvitingSelf_ReturnsForbiddenError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "inviter@example.com", // Same as inviter
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.CannotInviteSelf");
    }

    [Fact]
    public async Task Handle_WhenUserAlreadyMember_ReturnsConflictError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "existing@example.com",
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com");
        var existingUser = TestHelpers.CreateUser(id: existingUserId, email: "existing@example.com");
        var existingMembership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: existingUserId);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        _organizationRepositoryMock
            .Setup(r => r.GetMembershipAsync(orgId, existingUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Contain("AlreadyMember");
    }

    [Fact]
    public async Task Handle_WhenPendingInvitationExists_ReturnsConflictError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "pending@example.com",
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com");

        var pendingInvitation = TestHelpers.CreateOrganizationInvitation(
            organizationId: orgId,
            email: "pending@example.com",
            status: InvitationStatus.Pending);

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation> { pendingInvitation });

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Contain("PendingInvitation");
    }

    [Fact]
    public async Task Handle_InvitationTokenIsSecurelyGenerated()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "test@example.com",
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com", firstName: "John");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>());

        OrganizationInvitation? capturedInvitation = null;
        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .Callback<OrganizationInvitation, CancellationToken>((inv, _) => capturedInvitation = inv)
            .ReturnsAsync((OrganizationInvitation inv, CancellationToken _) => inv);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - token should be URL-safe Base64 and sufficiently long
        capturedInvitation.Should().NotBeNull();
        capturedInvitation!.Token.Should().NotBeNullOrEmpty();
        capturedInvitation.Token.Should().NotContain("+"); // URL-safe
        capturedInvitation.Token.Should().NotContain("/"); // URL-safe
        capturedInvitation.Token.Should().NotEndWith("="); // No padding
        capturedInvitation.Token.Length.Should().BeGreaterThanOrEqualTo(40); // Reasonable length for 32 bytes
    }

    [Fact]
    public async Task Handle_EmailIsNormalizedToLowercase()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "TEST@EXAMPLE.COM", // Uppercase email
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com", firstName: "John");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>());

        OrganizationInvitation? capturedInvitation = null;
        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .Callback<OrganizationInvitation, CancellationToken>((inv, _) => capturedInvitation = inv)
            .ReturnsAsync((OrganizationInvitation inv, CancellationToken _) => inv);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Email.Should().Be("test@example.com");
        capturedInvitation!.Email.Value.Should().Be("test@example.com");
    }

    [Fact]
    public async Task Handle_ValidInvitation_SendsInvitationEmail()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "newmember@example.com",
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com", firstName: "John", lastName: "Doe");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>());

        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation inv, CancellationToken _) => inv);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.TypeCode == NotificationTypeCodes.OrganizationInvitation &&
                r.RecipientAddress == "newmember@example.com" &&
                Equals(r.Variables["OrganizationName"], "Test Org") &&
                Equals(r.Variables["InviterName"], "John Doe") &&
                !string.IsNullOrEmpty((string?)r.Variables["InvitationToken"])),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailSendFails_StillReturnsInvitationWithToken()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "newmember@example.com",
            RoleId: roleId)
        { InvitedBy = inviterId };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var role = TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member");
        var inviter = TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com", firstName: "John", lastName: "Doe");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inviter);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>());

        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation inv, CancellationToken _) => inv);

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)NotificationErrors.SendFailed);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert - email failure must not fail the command; token stays available to admin
        result.IsError.Should().BeFalse();
        result.Value.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_WhenRoleIsApplicationScoped_ReturnsValidationError()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var command = new InviteMemberCommand(
            OrganizationId: orgId,
            Email: "test@example.com",
            RoleId: roleId)
        { InvitedBy = Guid.NewGuid() };

        var organization = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var appScopedRole = TestHelpers.CreateRole(
            id: roleId, applicationId: Guid.NewGuid(), code: "APP-ADMIN", name: "Administrator");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);

        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appScopedRole);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        result.FirstError.Code.Should().Be("Organization.InvalidMembershipRole");
        _organizationRepositoryMock.Verify(
            r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
