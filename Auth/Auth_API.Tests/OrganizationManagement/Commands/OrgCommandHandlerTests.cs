using Auth.Application.Features.Organizations.UpdateOrganization;
using Auth.Application.Features.Organizations.DeleteOrganization;
using Auth.Application.Features.Organizations.RemoveMember;
using Auth.Application.Features.Organizations.UpdateMemberRole;
using Auth.Application.Features.Organizations.ResendInvitation;
using Auth.Application.Features.Organizations.EnableApplication;
using Auth.Application.Features.Organizations.DisableApplication;
using Auth.Application.Features.Organizations.AssignAppRole;
using Auth.Application.Features.Organizations.GrantPermission;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

public class UpdateOrganizationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly UpdateOrganizationCommandHandler _handler;

    public UpdateOrganizationCommandHandlerTests()
    {
        _handler = new UpdateOrganizationCommandHandler(
            _orgRepoMock.Object,
            _userRepoMock.Object,
            new Mock<ILogger<UpdateOrganizationCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidData_UpdatesAndReturnsDto()
    {
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, name: "Old Name", ownerId: ownerId, isActive: true);
        var owner = TestHelpers.CreateUser(id: ownerId);
        var command = new UpdateOrganizationCommand(orgId, "New Name", "contact@test.com", "Desc", "https://logo.png", "https://web.com") { ModifiedBy = Guid.NewGuid() };

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembersAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationUser>());
        _orgRepoMock.Setup(r => r.GetEnabledApplicationsAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationApplication>());
        _userRepoMock.Setup(r => r.GetByIdAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.UpdateAsync(org, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        var result = await _handler.Handle(
            new UpdateOrganizationCommand(Guid.NewGuid(), "Name", "e@t.com") { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class DeleteOrganizationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly DeleteOrganizationCommandHandler _handler;

    public DeleteOrganizationCommandHandlerTests()
    {
        _handler = new DeleteOrganizationCommandHandler(
            _orgRepoMock.Object,
            new Mock<ILogger<DeleteOrganizationCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidOrg_OwnerDeletesSuccessfully()
    {
        var ownerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId);
        var command = new DeleteOrganizationCommand(orgId) { RequestedBy = ownerId };

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.DeleteAsync(orgId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsError()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        var result = await _handler.Handle(
            new DeleteOrganizationCommand(Guid.NewGuid()) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_NotOwner_ReturnsForbiddenError()
    {
        var ownerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId);
        var command = new DeleteOrganizationCommand(orgId) { RequestedBy = Guid.NewGuid() }; // Different user

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class RemoveMemberCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly RemoveMemberCommandHandler _handler;

    public RemoveMemberCommandHandlerTests()
    {
        _handler = new RemoveMemberCommandHandler(
            _orgRepoMock.Object,
            new Mock<ILogger<RemoveMemberCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidMember_RemovesSuccessfully()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId);
        var membership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);

        var result = await _handler.Handle(
            new RemoveMemberCommand(orgId, userId) { RemovedBy = ownerId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.RemoveMemberAsync(orgId, userId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_CannotRemoveOwner_ReturnsError()
    {
        var ownerId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);

        var result = await _handler.Handle(
            new RemoveMemberCommand(orgId, ownerId) { RemovedBy = ownerId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class UpdateMemberRoleCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly UpdateMemberRoleCommandHandler _handler;

    public UpdateMemberRoleCommandHandlerTests()
    {
        _handler = new UpdateMemberRoleCommandHandler(
            _orgRepoMock.Object,
            _roleRepoMock.Object,
            _userRepoMock.Object,
            new Mock<ILogger<UpdateMemberRoleCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidData_UpdatesRoleAndReturnsDto()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var user = TestHelpers.CreateUser(id: userId);
        var membership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId);
        var role = TestHelpers.CreateRole(id: newRoleId, name: "Admin");

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _userRepoMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _roleRepoMock.Setup(r => r.GetByIdAsync(newRoleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);

        var result = await _handler.Handle(
            new UpdateMemberRoleCommand(orgId, userId, newRoleId) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.UpdateMemberAsync(membership, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_MemberNotFound_ReturnsError()
    {
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser());
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrganizationUser?)null);

        var result = await _handler.Handle(
            new UpdateMemberRoleCommand(orgId, Guid.NewGuid(), Guid.NewGuid()) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ApplicationScopedRole_ReturnsValidationError()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var appScopedRole = TestHelpers.CreateRole(id: newRoleId, applicationId: Guid.NewGuid(), name: "Administrator");

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _roleRepoMock.Setup(r => r.GetByIdAsync(newRoleId, It.IsAny<CancellationToken>())).ReturnsAsync(appScopedRole);

        var result = await _handler.Handle(
            new UpdateMemberRoleCommand(orgId, userId, newRoleId) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Validation);
        result.FirstError.Code.Should().Be("Organization.InvalidMembershipRole");
        _orgRepoMock.Verify(r => r.UpdateMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_TargetIsOwner_ReturnsCannotChangeOwnerRole()
    {
        // An org-admin must not be able to demote the owner and seize control.
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId, isActive: true);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);

        var result = await _handler.Handle(
            new UpdateMemberRoleCommand(orgId, ownerId, newRoleId) { ModifiedBy = adminId },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.CannotChangeOwnerRole");
        _orgRepoMock.Verify(r => r.UpdateMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_AssigningOwnerRole_ReturnsCannotAssignOwnerRole()
    {
        // An org-admin must not be able to mint a new owner (org:*) — vertical
        // privilege escalation.
        var orgId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var newRoleId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var ownerRole = TestHelpers.CreateRole(
            id: newRoleId, code: Auth.Domain.Constants.OrganizationRoleCodes.Owner, name: "Organization Owner");

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _roleRepoMock.Setup(r => r.GetByIdAsync(newRoleId, It.IsAny<CancellationToken>())).ReturnsAsync(ownerRole);

        var result = await _handler.Handle(
            new UpdateMemberRoleCommand(orgId, targetId, newRoleId) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.CannotAssignOwnerRole");
        _orgRepoMock.Verify(r => r.UpdateMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}

public class ResendInvitationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ISecureTokenGenerator> _tokenGenMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly ResendInvitationCommandHandler _handler;

    private const string NewToken = "new-token-value-that-is-long-enough-for-validation";

    public ResendInvitationCommandHandlerTests()
    {
        _tokenGenMock.Setup(g => g.Generate()).Returns(NewToken);
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        _handler = new ResendInvitationCommandHandler(
            _orgRepoMock.Object,
            _roleRepoMock.Object,
            _userRepoMock.Object,
            _tokenGenMock.Object,
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(new Auth.Application.Configuration.EmailSettings
            {
                FrontendBaseUrl = "https://accounts.example.com"
            }),
            new Mock<ILogger<ResendInvitationCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidInvitation_ResendsSuccessfully()
    {
        var orgId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var resendBy = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var invitation = TestHelpers.CreateOrganizationInvitation(
            id: invId, organizationId: orgId, roleId: roleId, status: InvitationStatus.Pending);
        var role = TestHelpers.CreateRole(id: roleId, name: "Member");
        var resender = TestHelpers.CreateUser(id: resendBy);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetInvitationByIdAsync(invId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _userRepoMock.Setup(r => r.GetByIdAsync(resendBy, It.IsAny<CancellationToken>())).ReturnsAsync(resender);

        var result = await _handler.Handle(
            new ResendInvitationCommand(orgId, invId) { ResentBy = resendBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.UpdateInvitationAsync(invitation, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_InvitationNotFound_ReturnsError()
    {
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetInvitationByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrganizationInvitation?)null);

        var result = await _handler.Handle(
            new ResendInvitationCommand(orgId, Guid.NewGuid()) { ResentBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_ValidResend_SendsInvitationEmailWithNewToken()
    {
        var orgId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var resendBy = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, name: "Test Org", isActive: true);
        var invitation = TestHelpers.CreateOrganizationInvitation(
            id: invId, organizationId: orgId, email: "invited@example.com", roleId: roleId,
            invitedBy: resendBy, status: InvitationStatus.Pending);
        var role = TestHelpers.CreateRole(id: roleId, name: "Member");
        var resender = TestHelpers.CreateUser(id: resendBy, firstName: "John", lastName: "Doe");

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetInvitationByIdAsync(invId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _userRepoMock.Setup(r => r.GetByIdAsync(resendBy, It.IsAny<CancellationToken>())).ReturnsAsync(resender);

        var result = await _handler.Handle(
            new ResendInvitationCommand(orgId, invId) { ResentBy = resendBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Token.Should().Be(NewToken);
        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.RecipientAddress == "invited@example.com" &&
                Equals(r.Variables["OrganizationName"], "Test Org") &&
                Equals(r.Variables["InviterName"], "John Doe") &&
                Equals(r.Variables["InvitationToken"], NewToken)),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_EmailSendFails_StillReturnsInvitation()
    {
        var orgId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var invitation = TestHelpers.CreateOrganizationInvitation(
            id: invId, organizationId: orgId, status: InvitationStatus.Pending);

        _orgRepoMock.Setup(r => r.GetInvitationByIdAsync(invId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)NotificationErrors.SendFailed);

        var result = await _handler.Handle(
            new ResendInvitationCommand(orgId, invId) { ResentBy = Guid.NewGuid() },
            CancellationToken.None);

        // Email failure must not fail the command; token stays available to admin
        result.IsError.Should().BeFalse();
        result.Value.Token.Should().Be(NewToken);
    }

    [Fact]
    public async Task Handle_NonPendingInvitation_ReturnsErrorAndDoesNotSendEmail()
    {
        var orgId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var invitation = TestHelpers.CreateOrganizationInvitation(
            id: invId, organizationId: orgId, status: InvitationStatus.Accepted);

        _orgRepoMock.Setup(r => r.GetInvitationByIdAsync(invId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);

        var result = await _handler.Handle(
            new ResendInvitationCommand(orgId, invId) { ResentBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvitationNotPending");
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}


public class DisableApplicationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly DisableApplicationCommandHandler _handler;

    public DisableApplicationCommandHandlerTests()
    {
        _handler = new DisableApplicationCommandHandler(
            _orgRepoMock.Object,
            _appRepoMock.Object,
            new Mock<ILogger<DisableApplicationCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidData_DisablesApplication()
    {
        var orgId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var app = TestHelpers.CreateApplication(id: appId);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(
            new DisableApplicationCommand(orgId, appId) { DisabledBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.DisableApplicationAsync(orgId, appId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_OrgNotFound_ReturnsError()
    {
        _orgRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Organization?)null);

        var result = await _handler.Handle(
            new DisableApplicationCommand(Guid.NewGuid(), Guid.NewGuid()) { DisabledBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class AssignAppRoleCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly AssignAppRoleCommandHandler _handler;

    public AssignAppRoleCommandHandlerTests()
    {
        _handler = new AssignAppRoleCommandHandler(
            _orgRepoMock.Object,
            _appRepoMock.Object,
            _roleRepoMock.Object,
            _userRepoMock.Object,
            new Mock<ILogger<AssignAppRoleCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidData_AssignsRole()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var membership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId);
        var app = TestHelpers.CreateApplication(id: appId);
        var role = TestHelpers.CreateRole(id: roleId, applicationId: appId);
        var user = TestHelpers.CreateUser(id: userId);
        var assignerUser = TestHelpers.CreateUser(id: assignedBy);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>())).ReturnsAsync(role);
        _userRepoMock.Setup(r => r.GetByIdAsync(assignedBy, It.IsAny<CancellationToken>())).ReturnsAsync(assignerUser);
        _orgRepoMock.Setup(r => r.HasAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _orgRepoMock.Setup(r => r.AssignAppRoleAsync(It.IsAny<OrganizationUserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUserRole assignment, CancellationToken _) => assignment);

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, userId, appId, roleId) { AssignedBy = assignedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MemberNotFound_ReturnsError()
    {
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrganizationUser?)null);

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) { AssignedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class OrgGrantPermissionCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<IPermissionRepository> _permRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly GrantPermissionCommandHandler _handler;

    public OrgGrantPermissionCommandHandlerTests()
    {
        _handler = new GrantPermissionCommandHandler(
            _orgRepoMock.Object,
            _appRepoMock.Object,
            _permRepoMock.Object,
            _userRepoMock.Object,
            new Mock<ILogger<GrantPermissionCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidData_GrantsPermission()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);
        var membership = TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId);
        var app = TestHelpers.CreateApplication(id: appId);
        var perm = TestHelpers.CreatePermission(id: permId, applicationId: appId);
        var user = TestHelpers.CreateUser(id: userId);
        var granterUser = TestHelpers.CreateUser(id: grantedBy);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        _permRepoMock.Setup(r => r.GetByIdAsync(permId, It.IsAny<CancellationToken>())).ReturnsAsync(perm);
        _userRepoMock.Setup(r => r.GetByIdAsync(grantedBy, It.IsAny<CancellationToken>())).ReturnsAsync(granterUser);
        _orgRepoMock.Setup(r => r.HasPermissionAsync(orgId, userId, appId, permId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _orgRepoMock.Setup(r => r.GrantPermissionAsync(It.IsAny<OrganizationUserPermission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUserPermission grant, CancellationToken _) => grant);

        var result = await _handler.Handle(
            new GrantPermissionCommand(orgId, userId, appId, permId) { GrantedBy = grantedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MemberNotFound_ReturnsError()
    {
        var orgId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, isActive: true);

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((OrganizationUser?)null);

        var result = await _handler.Handle(
            new GrantPermissionCommand(orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) { GrantedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
