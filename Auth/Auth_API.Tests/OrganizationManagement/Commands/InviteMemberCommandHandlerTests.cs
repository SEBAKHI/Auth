using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.InviteMember;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.ApplicationManagement.Commands;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
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
    private const string GeneratedToken = "dGVzdC10b2tlbi1mb3ItaW52aXRhdGlvbi10aGF0LWlzLWxvbmctZW5vdWdo";

    private readonly Mock<ISecureTokenGenerator> _tokenGeneratorMock;
    private readonly FakeCredentialHasher _tokenKeyServiceMock = new();
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
            .Returns(GeneratedToken);

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        // A hash that is visibly not its input, so a test asserting "the stored
        // value is not the token" cannot pass by accident.
        _tokenKeyServiceMock.MapAll(token => $"HASHED({token})");

        _handler = new InviteMemberCommandHandler(
            _organizationRepositoryMock.Object,
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _tokenGeneratorMock.Object,
            _tokenKeyServiceMock,
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(new EmailSettings { FrontendBaseUrl = "https://accounts.example.com" }),
            ApplicationTestImages.Composer(),
            new OrganizationApplicationRoleCheck(
                _organizationRepositoryMock.Object,
                _applicationRepositoryMock.Object,
                _roleRepositoryMock.Object,
                _permissionRepositoryMock.Object,
                new OrganizationGrantGuard(_organizationRepositoryMock.Object, _permissionRepositoryMock.Object)),
            _loggerMock.Object);
    }

    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();

    [Fact]
    public async Task Handle_InvitingAsAPlatformRole_ReturnsInvalidMembershipRole()
    {
        // A platform role has no application either; binding one as a membership role
        // grants nothing (membership authority is read through an org: filter), so the
        // invitation is refused instead of producing a member who can do nothing.
        var (command, _) = ArrangeInvitableOrganization(
            TestHelpers.CreateRole(code: "super-admin", name: "Super Administrator"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvalidMembershipRole");
        _organizationRepositoryMock.Verify(
            r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_NamingAnApplicationRoleWithoutTheRightToAssignOne_ReturnsInsufficientPermissions()
    {
        var (command, _) = ArrangeInvitableOrganization(MemberRole());
        var (applicationId, appRoleId) = ArrangeApplicationRole(command.OrganizationId, "crm:leads:read");

        var result = await _handler.Handle(
            command with
            {
                ApplicationRole = new InviteApplicationRole(applicationId, appRoleId),
                CanAssignApplicationRoles = false
            },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InsufficientPermissions");
    }

    [Fact]
    public async Task Handle_NamingAnApplicationRoleCarryingMoreThanTheInviterHolds_ReturnsCannotGrantHigher()
    {
        var (command, _) = ArrangeInvitableOrganization(MemberRole());
        var (applicationId, appRoleId) = ArrangeApplicationRole(command.OrganizationId, "crm:leads:manage");
        InviterHolds(command, applicationId, "crm:leads:read");

        var result = await _handler.Handle(
            command with
            {
                ApplicationRole = new InviteApplicationRole(applicationId, appRoleId),
                CanAssignApplicationRoles = true
            },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Permission.CannotGrantHigher");
        _organizationRepositoryMock.Verify(
            r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_NamingAnApplicationRoleTheInviterCouldAssign_StoresItOnTheInvitation()
    {
        var (command, _) = ArrangeInvitableOrganization(MemberRole());
        var (applicationId, appRoleId) = ArrangeApplicationRole(command.OrganizationId, "crm:leads:read");
        InviterHolds(command, applicationId, "crm:*");
        OrganizationInvitation? stored = null;
        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .Callback<OrganizationInvitation, CancellationToken>((invitation, _) => stored = invitation)
            .ReturnsAsync((OrganizationInvitation invitation, CancellationToken _) => invitation);

        var result = await _handler.Handle(
            command with
            {
                ApplicationRole = new InviteApplicationRole(applicationId, appRoleId),
                CanAssignApplicationRoles = true
            },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        stored!.ApplicationRole.Should().Be(InvitationApplicationRole.From(applicationId, appRoleId));
        result.Value.ApplicationId.Should().Be(applicationId);
        result.Value.ApplicationRoleId.Should().Be(appRoleId);
        result.Value.ApplicationRoleName.Should().Be("Sales representative");
    }

    private static Role MemberRole() =>
        TestHelpers.CreateRole(code: OrganizationRoleCodes.Member, name: "Organization Member");

    private (InviteMemberCommand Command, Guid InviterId) ArrangeInvitableOrganization(Role membershipRole)
    {
        var orgId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(membershipRole.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membershipRole);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com"));
        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>());
        _organizationRepositoryMock
            .Setup(r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationInvitation invitation, CancellationToken _) => invitation);

        return (new InviteMemberCommand(orgId, "rep@example.com", membershipRole.Id) { InvitedBy = inviterId }, inviterId);
    }

    private (Guid ApplicationId, Guid RoleId) ArrangeApplicationRole(Guid orgId, string permissionCode)
    {
        var applicationId = Guid.NewGuid();
        var role = TestHelpers.CreateRole(applicationId: applicationId, name: "Sales representative");
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId));
        _organizationRepositoryMock
            .Setup(r => r.IsApplicationEnabledAsync(orgId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);
        _permissionRepositoryMock
            .Setup(r => r.GetRolePermissionsAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreatePermission(applicationId: applicationId, code: permissionCode)]);
        return (applicationId, role.Id);
    }

    private void InviterHolds(InviteMemberCommand command, Guid applicationId, params string[] codes)
    {
        _organizationRepositoryMock
            .Setup(r => r.GetEffectivePermissionCodesAsync(
                command.OrganizationId, command.InvitedBy, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codes);
    }

    [Fact]
    public async Task Handle_InvitingAsOwnerRole_ReturnsCannotAssignOwnerRole()
    {
        // An org:members:invite holder must not mint an owner by inviting a
        // controlled account as org-owner (same escalation as UpdateMemberRole).
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var organization = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var ownerRole = TestHelpers.CreateRole(
            id: roleId, code: OrganizationRoleCodes.Owner, name: "Organization Owner");

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(organization);
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ownerRole);

        var result = await _handler.Handle(
            new InviteMemberCommand(orgId, "newmember@example.com", roleId) { InvitedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.CannotAssignOwnerRole");
        _organizationRepositoryMock.Verify(
            r => r.CreateInvitationAsync(It.IsAny<OrganizationInvitation>(), It.IsAny<CancellationToken>()),
            Times.Never());
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
    public async Task Handle_OrganizationWithUploadedLogoKey_ReturnsComposedOrganizationLogoUrl()
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

        _organizationRepositoryMock
            .Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true, logoUrl: "acme.webp"));
        _roleRepositoryMock
            .Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member"));
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com"));
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
        result.Value.OrganizationLogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/acme.webp");
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

    /// <summary>
    /// OI-193 T7: the list now shows a stored-pending invitation past its expiry as Expired,
    /// but the duplicate check keeps its rule, so that row still blocks a second invitation
    /// (the list offers Resend for it instead), and the check still reads the stored-pending
    /// query, not the list's filtered one.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAStoredPendingInvitationHasExpired_StillReturnsPendingInvitationExists()
    {
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var command = new InviteMemberCommand(OrganizationId: orgId, Email: "pending@example.com", RoleId: roleId)
        { InvitedBy = inviterId };

        _organizationRepositoryMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true));
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, code: "ORG-MEMBER", name: "Member"));
        _userRepositoryMock.Setup(r => r.GetByIdAsync(inviterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: inviterId, email: "inviter@example.com"));
        _userRepositoryMock.Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _organizationRepositoryMock
            .Setup(r => r.GetPendingInvitationsAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrganizationInvitation>
            {
                TestHelpers.CreateOrganizationInvitation(
                    organizationId: orgId,
                    email: "pending@example.com",
                    status: InvitationStatus.Pending,
                    expiresAt: DateTime.UtcNow.AddDays(-3))
            });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(OrganizationErrors.PendingInvitationExists("pending@example.com").Code);
        _organizationRepositoryMock.Verify(r => r.GetInvitationsPagedAsync(
            It.IsAny<Guid>(), It.IsAny<Auth.Domain.ReadModels.Organizations.InvitationListFilter>(), It.IsAny<DateTime>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Auth.Domain.Enums.SortDirection>(),
            It.IsAny<CancellationToken>()), Times.Never());
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

        // Assert - what is PERSISTED is the hash, never the token itself. This
        // used to assert the opposite (that the stored value was URL-safe base64
        // of the right length), which was an accurate description of a defect:
        // the invitation token was the one bearer credential in this system kept
        // in clear text, so a single SELECT on this table was a working
        // invitation into the organization it named.
        capturedInvitation.Should().NotBeNull();
        capturedInvitation!.TokenHash.Should().NotBeNullOrEmpty();
        capturedInvitation.TokenHash.Should().NotBe(GeneratedToken,
            "the plaintext token belongs in the e-mail and nowhere else");
        capturedInvitation.TokenHash.Should().Be($"HASHED({GeneratedToken})",
            "the stored value must come from the shared token-hashing service, " +
            "not from some second hashing scheme invented at this call site");
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

        // Assert - email failure must not fail the command. The invitation exists
        // and can be resent; what must NOT happen is the token coming back to the
        // caller as a manual fallback, which is the shape this once had.
        result.IsError.Should().BeFalse();
        result.Value.Id.Should().NotBeEmpty();
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
