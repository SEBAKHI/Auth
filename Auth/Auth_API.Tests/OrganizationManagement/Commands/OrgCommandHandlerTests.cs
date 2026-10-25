using Auth.Application.Features.Organizations.UpdateOrganization;
using Auth.Application.Features.Organizations.DeleteOrganization;
using Auth.Application.Features.Organizations.RemoveMember;
using Auth.Application.Features.Organizations.UpdateMemberRole;
using Auth.Application.Features.Organizations.ResendInvitation;
using Auth.Application.Features.Organizations.EnableApplication;
using Auth.Application.Features.Organizations.DisableApplication;
using Auth.Application.Features.Organizations.AssignAppRole;
using Auth.Application.Features.Organizations.GrantPermission;
using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Organizations;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.ApplicationManagement.Commands;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.OrganizationManagement.Commands;

public class UpdateOrganizationCommandHandlerTests
{
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IUploadedImageRepository> _uploadedImagesMock = new();
    private readonly UpdateOrganizationCommandHandler _handler;

    public UpdateOrganizationCommandHandlerTests()
    {
        _handler = new UpdateOrganizationCommandHandler(
            _orgRepoMock.Object,
            _userRepoMock.Object,
            ApplicationTestImages.Composer(),
            ApplicationTestImages.Guard(_uploadedImagesMock),
            new Mock<ILogger<UpdateOrganizationCommandHandler>>().Object);
    }

    /// <summary>The organization, its owner and the response lookups.</summary>
    private Organization ArrangeOrganization(string? logoUrl = null)
    {
        var ownerId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(ownerId: ownerId, isActive: true, logoUrl: logoUrl);

        _orgRepoMock.Setup(r => r.GetByIdAsync(org.Id, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembersAsync(org.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationUser>());
        _orgRepoMock.Setup(r => r.GetEnabledApplicationsAsync(org.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationApplication>());
        _userRepoMock.Setup(r => r.GetByIdAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: ownerId));

        return org;
    }

    [Fact]
    public async Task Handle_ComposedUrlOfOwnUpload_StoresTheKeyDecomposedAndClaimsIt()
    {
        var org = ArrangeOrganization();
        var actor = Guid.NewGuid();
        _uploadedImagesMock
            .Setup(r => r.TryClaimAsync("acme.webp", actor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new UpdateOrganizationCommand(org.Id, "Acme", "contact@test.com",
                LogoUrl: $"{ApplicationTestImages.PublicBaseUrl}/acme.webp") { ModifiedBy = actor },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        org.LogoUrl.Should().Be("acme.webp");
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync("acme.webp", actor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AnotherUsersUploadKey_ReturnsImageNotAvailableAndSavesNothing()
    {
        var org = ArrangeOrganization(logoUrl: "previous.webp");
        var actor = Guid.NewGuid();

        var result = await _handler.Handle(
            new UpdateOrganizationCommand(org.Id, "Acme", "contact@test.com",
                LogoUrl: "someone-elses.webp") { ModifiedBy = actor },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ImageErrors.NotAvailable.Code);
        org.LogoUrl.Should().Be("previous.webp");
        _orgRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Never);
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync("someone-elses.webp", actor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UnchangedLegacyComposedLogo_SavesWithoutAClaim()
    {
        // A row written before key normalization holds the composed URL, and
        // the form resends it as read. Renaming the organization must not fail
        // on a logo nobody changed.
        var legacy = $"{ApplicationTestImages.PublicBaseUrl}/acme.webp";
        var org = ArrangeOrganization(logoUrl: legacy);

        var result = await _handler.Handle(
            new UpdateOrganizationCommand(org.Id, "Acme renamed", "contact@test.com",
                LogoUrl: legacy) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        org.LogoUrl.Should().Be("acme.webp");
        _uploadedImagesMock.Verify(
            r => r.TryClaimAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _orgRepoMock.Verify(
            r => r.UpdateAsync(It.IsAny<Organization>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task Handle_WithUploadedLogoKey_StoresKeyAndReturnsComposedLogoUrl()
    {
        var orgId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var org = TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId, isActive: true);
        var command = new UpdateOrganizationCommand(orgId, "Acme", "contact@test.com", LogoUrl: "acme.webp") { ModifiedBy = Guid.NewGuid() };

        _uploadedImagesMock
            .Setup(r => r.TryClaimAsync("acme.webp", command.ModifiedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(org);
        _orgRepoMock.Setup(r => r.GetMembersAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationUser>());
        _orgRepoMock.Setup(r => r.GetEnabledApplicationsAsync(orgId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<OrganizationApplication>());
        _userRepoMock.Setup(r => r.GetByIdAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: ownerId));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.LogoUrl.Should().Be($"{ApplicationTestImages.PublicBaseUrl}/acme.webp");
        org.LogoUrl.Should().Be("acme.webp");
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
    private readonly Mock<MediatR.IPublisher> _publisherMock = new();
    private readonly RemoveMemberCommandHandler _handler;

    public RemoveMemberCommandHandlerTests()
    {
        _handler = new RemoveMemberCommandHandler(
            _orgRepoMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<RemoveMemberCommandHandler>>().Object);
    }

    private (Guid OrgId, Guid UserId, Guid OwnerId) WireMember()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId));
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));
        return (orgId, userId, ownerId);
    }

    /// <summary>
    /// OI-231 T2 (V2): a removal that removed the membership is recorded once, with the
    /// application roles and permissions the repository deleted with it. Deliberate break:
    /// publish whatever the repository answered.
    /// </summary>
    [Fact]
    public async Task Handle_ValidMember_RemovesAndPublishesWhatWentWithIt()
    {
        var (orgId, userId, ownerId) = WireMember();
        var role = new RemovedOrganizationApplicationRole(Guid.NewGuid(), Guid.NewGuid(), "crm:agent", "Agent");
        var permission = new RemovedOrganizationPermission(role.ApplicationId, Guid.NewGuid(), "crm:tickets:read");
        _orgRepoMock.Setup(r => r.RemoveMemberAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationMemberRemoval(true, [role], [permission]));

        var result = await _handler.Handle(
            new RemoveMemberCommand(orgId, userId) { RemovedBy = ownerId },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _orgRepoMock.Verify(r => r.RemoveMemberAsync(orgId, userId, It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<OrganizationMemberRemovedEvent>(e =>
                    e.OrganizationId == orgId && e.UserId == userId && e.RemovedBy == ownerId
                    && e.RemovedApplicationRoles.SequenceEqual(new[] { role })
                    && e.RemovedPermissions.SequenceEqual(new[] { permission })),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// OI-231 T1: the membership was gone by the time the statement ran (a concurrent
    /// removal): nothing was deleted, nothing is recorded, and the answer is the one the
    /// ordinary check gives.
    /// </summary>
    [Fact]
    public async Task Handle_MembershipNotRemoved_ReturnsNotMember_AndPublishesNothing()
    {
        var (orgId, userId, ownerId) = WireMember();
        _orgRepoMock.Setup(r => r.RemoveMemberAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationMemberRemoval(false, [], []));

        var result = await _handler.Handle(
            new RemoveMemberCommand(orgId, userId) { RemovedBy = ownerId },
            CancellationToken.None);

        result.FirstError.Code.Should().Be(OrganizationErrors.NotMember(userId, orgId).Code);
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _publisherMock.Invocations.Should().BeEmpty();
    }

    /// <summary>
    /// OI-231 T1: ownership moved to the person between the check and the statement, which
    /// refused to delete an owner's membership: the answer is the owner refusal.
    /// </summary>
    [Fact]
    public async Task Handle_BecameOwnerBeforeTheStatement_ReturnsCannotRemoveOwner_AndPublishesNothing()
    {
        var (orgId, userId, ownerId) = WireMember();
        _orgRepoMock.SetupSequence(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, ownerId: ownerId))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, ownerId: userId));
        _orgRepoMock.Setup(r => r.RemoveMemberAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrganizationMemberRemoval(false, [], []));

        var result = await _handler.Handle(
            new RemoveMemberCommand(orgId, userId) { RemovedBy = ownerId },
            CancellationToken.None);

        result.FirstError.Code.Should().Be(OrganizationErrors.CannotRemoveOwner.Code);
        _publisherMock.Invocations.Should().BeEmpty();
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

        result.FirstError.Code.Should().Be(OrganizationErrors.CannotRemoveOwner.Code);
        _orgRepoMock.Verify(r => r.RemoveMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Invocations.Should().BeEmpty();
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
        var role = TestHelpers.CreateRole(id: newRoleId, code: Auth.Domain.Constants.OrganizationRoleCodes.Admin, name: "Admin");

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
    public async Task Handle_PlatformRoleAsMembershipRole_ReturnsInvalidMembershipRole()
    {
        // A platform role has no application either; the role-change picker never
        // offers one, and the write now refuses it the same way.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var platformRole = TestHelpers.CreateRole(code: "super-admin", name: "Super Administrator");

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _roleRepoMock.Setup(r => r.GetByIdAsync(platformRole.Id, It.IsAny<CancellationToken>())).ReturnsAsync(platformRole);

        var result = await _handler.Handle(
            new UpdateMemberRoleCommand(orgId, userId, platformRole.Id) { ModifiedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Organization.InvalidMembershipRole");
        _orgRepoMock.Verify(r => r.UpdateMemberAsync(It.IsAny<OrganizationUser>(), It.IsAny<CancellationToken>()), Times.Never());
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
    private readonly Mock<IApplicationRepository> _appRepoMock = new();
    private readonly Mock<IOrganizationRepository> _orgRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<ISecureTokenGenerator> _tokenGenMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly ResendInvitationCommandHandler _handler;

    private const string NewToken = "new-token-value-that-is-long-enough-for-validation";

    private readonly FakeCredentialHasher _tokenKeyServiceMock = new();

    public ResendInvitationCommandHandlerTests()
    {
        _tokenGenMock.Setup(g => g.Generate()).Returns(NewToken);

        // Visibly not its input, so an assertion that the stored value is the
        // hash rather than the token cannot pass by accident.
        _tokenKeyServiceMock.MapAll(token => $"HASHED({token})");

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
        _handler = new ResendInvitationCommandHandler(
            _orgRepoMock.Object,
            _roleRepoMock.Object,
            _userRepoMock.Object,
            _appRepoMock.Object,
            _tokenGenMock.Object,
            _tokenKeyServiceMock,
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(new Auth.Application.Configuration.EmailSettings
            {
                FrontendBaseUrl = "https://accounts.example.com"
            }),
            new Mock<ILogger<ResendInvitationCommandHandler>>().Object,
            TimeProvider.System);
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

    /// <summary>
    /// OI-193 T6: the resend response describes the invitation as the invite response does,
    /// application role included, and a resent invitation is live and can be resent again.
    /// </summary>
    [Fact]
    public async Task Handle_ResendingAnExpiredInvitationNamingAnApplicationRole_ReturnsItLive_WithTheApplicationFields()
    {
        var orgId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(name: "Customer Desk");
        var applicationRole = TestHelpers.CreateRole(name: "Sales representative");
        var invitation = TestHelpers.CreateOrganizationInvitation(
            id: invId, organizationId: orgId, status: InvitationStatus.Pending,
            expiresAt: DateTime.UtcNow.AddDays(-2),
            applicationRole: Auth.Domain.ValueObjects.InvitationApplicationRole.From(application.Id, applicationRole.Id));

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _orgRepoMock.Setup(r => r.GetInvitationByIdAsync(invId, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        _roleRepoMock.Setup(r => r.GetByIdAsync(applicationRole.Id, It.IsAny<CancellationToken>())).ReturnsAsync(applicationRole);
        _appRepoMock.Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(application);

        var result = await _handler.Handle(
            new ResendInvitationCommand(orgId, invId) { ResentBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationId.Should().Be(application.Id);
        result.Value.ApplicationName.Should().Be("Customer Desk");
        result.Value.ApplicationRoleId.Should().Be(applicationRole.Id);
        result.Value.ApplicationRoleName.Should().Be("Sales representative");
        result.Value.Status.Should().Be("Pending", "resending reset the expiry");
        result.Value.IsExpired.Should().BeFalse();
        result.Value.CanResend.Should().BeTrue();
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

        // The regenerated plaintext goes to the invited mailbox and nowhere else.
        // The caller gets the invitation's identity; the Verify below is where the
        // token itself is asserted, in the notification addressed to the invitee.
        // (That the DTO cannot carry a token at all is held by
        // InvitationResponseGuardTests, which checks the type rather than a case.)
        result.Value.Id.Should().Be(invId);
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

        // Email failure must not fail the command. The regenerated invitation
        // stands and can be resent; the token is not handed back as a manual
        // fallback, which is the shape this assertion used to hold.
        result.IsError.Should().BeFalse();
        result.Value.Id.Should().Be(invId);
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
    private readonly Mock<IPermissionRepository> _permRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<MediatR.IPublisher> _publisherMock = new();
    private readonly AssignAppRoleCommandHandler _handler;

    public AssignAppRoleCommandHandlerTests()
    {
        // Assigning a role hands over its permissions, so the handler now asks
        // whether the actor holds them. These defaults put the actor in the
        // ordinary case — a platform operator carrying the global wildcard —
        // so the existing cases keep testing what they were written to test.
        _permRepoMock
            .Setup(r => r.GetRolePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Permission>());
        _permRepoMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "*" });
        _orgRepoMock
            .Setup(r => r.GetEffectivePermissionCodesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _handler = new AssignAppRoleCommandHandler(
            _orgRepoMock.Object,
            _userRepoMock.Object,
            new OrganizationApplicationRoleCheck(
                _orgRepoMock.Object,
                _appRepoMock.Object,
                _roleRepoMock.Object,
                _permRepoMock.Object,
                new OrganizationGrantGuard(_orgRepoMock.Object, _permRepoMock.Object)),
            _publisherMock.Object,
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
            .ReturnsAsync((OrganizationUserRole assignment, CancellationToken _) => assignment.Id);

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, userId, appId, roleId) { AssignedBy = assignedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    /// <summary>
    /// OI-225 (1): the write is the repository's update-or-insert, the answer carries the
    /// id of the row that holds the assignment (a re-assigned expired row keeps its own),
    /// and the assignment is recorded. Deliberate break: drop the publish.
    /// </summary>
    [Fact]
    public async Task Handle_Assigned_AnswersTheStoredRowAndPublishes()
    {
        var (orgId, userId, appId, roleId, assignedBy) = WireAssignable();
        var storedRowId = Guid.NewGuid();
        _orgRepoMock.Setup(r => r.HasAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _orgRepoMock.Setup(r => r.AssignAppRoleAsync(It.IsAny<OrganizationUserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedRowId);
        var expiresAt = DateTime.UtcNow.AddDays(10);

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, userId, appId, roleId, expiresAt) { AssignedBy = assignedBy },
            CancellationToken.None);

        result.Value.Id.Should().Be(storedRowId);
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<OrganizationAppRoleAssignedEvent>(e =>
                    e.OrganizationId == orgId && e.UserId == userId && e.ApplicationId == appId
                    && e.RoleId == roleId && e.AssignedBy == assignedBy && e.ExpiresAt == expiresAt),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// OI-231 T5 (V8): the membership was removed between the handler's check and the write;
    /// the statement checks it again under its own lock, writes nothing and answers null,
    /// which is the ordinary "not a member". Nothing is recorded. Deliberate break: map
    /// null to success.
    /// </summary>
    [Fact]
    public async Task Handle_MembershipRemovedBeforeTheWrite_ReturnsNotMember_AndRecordsNothing()
    {
        var (orgId, userId, appId, roleId, assignedBy) = WireAssignable();
        _orgRepoMock.Setup(r => r.HasAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _orgRepoMock.Setup(r => r.AssignAppRoleAsync(It.IsAny<OrganizationUserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, userId, appId, roleId) { AssignedBy = assignedBy },
            CancellationToken.None);

        result.FirstError.Code.Should().Be(OrganizationErrors.NotMember(userId, orgId).Code);
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
        _publisherMock.Invocations.Should().BeEmpty();
    }

    /// <summary>Only a valid assignment (active, unexpired) refuses: nothing written, nothing recorded.</summary>
    [Fact]
    public async Task Handle_AValidAssignmentExists_Returns409_WritesAndRecordsNothing()
    {
        var (orgId, userId, appId, roleId, assignedBy) = WireAssignable();
        _orgRepoMock.Setup(r => r.HasAppRoleAsync(orgId, userId, appId, roleId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, userId, appId, roleId) { AssignedBy = assignedBy },
            CancellationToken.None);

        result.FirstError.Code.Should().Be("Organization.AppRoleAlreadyAssigned");
        result.FirstError.Type.Should().Be(ErrorType.Conflict);
        _orgRepoMock.Verify(r => r.AssignAppRoleAsync(It.IsAny<OrganizationUserRole>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Invocations.Should().BeEmpty();
    }

    private (Guid OrgId, Guid UserId, Guid AppId, Guid RoleId, Guid AssignedBy) WireAssignable()
    {
        var (orgId, userId, appId, roleId, assignedBy) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateApplication(id: appId));
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: appId));
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return (orgId, userId, appId, roleId, assignedBy);
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

    [Fact]
    public async Task Handle_ActorDoesNotHoldWhatTheRoleCarries_IsRefused()
    {
        // Assigning a role is granting its permissions by another name, and it
        // has the widest blast radius of the two paths. Until the guard landed,
        // org:permissions:manage was enough to assign a member — or oneself —
        // any application role the organization had enabled.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignedBy = Guid.NewGuid();

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId));
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _roleRepoMock.Setup(r => r.GetByIdAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRole(id: roleId, applicationId: appId));

        // The role carries authority the actor does not hold anywhere.
        _permRepoMock.Setup(r => r.GetRolePermissionsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestHelpers.CreatePermission(code: "crm:admin", applicationId: appId) });
        _permRepoMock.Setup(r => r.GetUserEffectivePermissionsAsync(assignedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "org:permissions:manage" });
        _orgRepoMock.Setup(r => r.GetEffectivePermissionCodesAsync(
                orgId, assignedBy, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "crm:leads:read" });

        var result = await _handler.Handle(
            new AssignAppRoleCommand(orgId, userId, appId, roleId) { AssignedBy = assignedBy },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        _orgRepoMock.Verify(
            r => r.AssignAppRoleAsync(It.IsAny<OrganizationUserRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        // The handler now asks whether the actor holds what it is handing over.
        // These defaults put the actor in the ordinary case — a platform
        // operator carrying the global wildcard — so the existing cases keep
        // testing what they were written to test.
        _permRepoMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "*" });
        _orgRepoMock
            .Setup(r => r.GetEffectivePermissionCodesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _handler = new GrantPermissionCommandHandler(
            _orgRepoMock.Object,
            _appRepoMock.Object,
            _permRepoMock.Object,
            _userRepoMock.Object,
            new OrganizationGrantGuard(_orgRepoMock.Object, _permRepoMock.Object),
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
            new GrantPermissionCommand(orgId, userId, appId, permId) { GrantedBy = grantedBy, PlatformAuthorityInToken = true },
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

    [Fact]
    public async Task Handle_ActorDoesNotHoldThePermission_IsRefused()
    {
        // The endpoint gate asks who may grant; this asks what may be granted.
        // Without it, org:permissions:manage — which the seeded org-admin role
        // carries — was enough to hand any member, itself included, every
        // permission of every enabled application.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId));
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _permRepoMock.Setup(r => r.GetByIdAsync(permId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePermission(id: permId, code: "crm:admin", applicationId: appId));

        // The actor administers grants here, but holds only a narrow code.
        _permRepoMock.Setup(r => r.GetUserEffectivePermissionsAsync(grantedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "org:permissions:manage" });
        _orgRepoMock.Setup(r => r.GetEffectivePermissionCodesAsync(
                orgId, grantedBy, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "crm:leads:read" });

        var result = await _handler.Handle(
            new GrantPermissionCommand(orgId, userId, appId, permId) { GrantedBy = grantedBy },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        _orgRepoMock.Verify(
            r => r.GrantPermissionAsync(It.IsAny<OrganizationUserPermission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ActorHoldsItInTheOrganization_IsAllowed()
    {
        // The other half of the boundary: an organization administrator that
        // does hold the code must still be able to delegate it, or the fix
        // would have removed the feature rather than bounded it.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var grantedBy = Guid.NewGuid();

        _orgRepoMock.Setup(r => r.GetByIdAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganization(id: orgId, isActive: true));
        _orgRepoMock.Setup(r => r.GetMembershipAsync(orgId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: userId));
        _appRepoMock.Setup(r => r.GetByIdAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(id: appId));
        _orgRepoMock.Setup(r => r.IsApplicationEnabledAsync(orgId, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _permRepoMock.Setup(r => r.GetByIdAsync(permId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreatePermission(id: permId, code: "crm:leads:read", applicationId: appId));
        _orgRepoMock.Setup(r => r.HasPermissionAsync(orgId, userId, appId, permId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _userRepoMock.Setup(r => r.GetByIdAsync(grantedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: grantedBy));
        _orgRepoMock.Setup(r => r.GrantPermissionAsync(It.IsAny<OrganizationUserPermission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrganizationUserPermission grant, CancellationToken _) => grant);

        _permRepoMock.Setup(r => r.GetUserEffectivePermissionsAsync(grantedBy, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _orgRepoMock.Setup(r => r.GetEffectivePermissionCodesAsync(
                orgId, grantedBy, appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "crm:*" });

        var result = await _handler.Handle(
            new GrantPermissionCommand(orgId, userId, appId, permId) { GrantedBy = grantedBy },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
    }
}
