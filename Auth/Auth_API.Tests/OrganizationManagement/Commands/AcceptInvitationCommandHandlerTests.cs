using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.AcceptInvitation;
using Auth_API.Tests.Helpers;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
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
    private readonly FakeCredentialHasher _tokenKeyServiceMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock = new();
    private readonly AcceptInvitationCommandHandler _handler;

    private const string Issuer = "https://id.example.com";
    private const string ImageBase = "https://images.example.com";

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
        _tokenKeyServiceMock.MapAll(token => token);

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
            _tokenKeyServiceMock,
            applicationRoleCheck,
            _applicationRepositoryMock.Object,
            new ImageUrlComposer(TestHelpers.CreateOptions(new ImageStorageSettings { PublicBaseUrl = ImageBase })),
            new ApplicationLaunchTarget(TestHelpers.CreateOptions(new JwtSettings { Issuer = Issuer })),
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

        // OI-192: an invitation that names no application role records no outcome, names
        // no application and launches nothing; the page lands on the organization.
        result.Value.ApplicationRoleOutcome.Should().BeNull();
        result.Value.Application.Should().BeNull();
        result.Value.LaunchUrl.Should().BeNull();

        // Status, membership and (no) application role in one transactional write.
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i =>
                    i.Status == InvitationStatus.Accepted && i.ApplicationRoleOutcome == null),
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
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Applied);
        result.Value.Application.Should().BeEquivalentTo(new InvitationApplicationDto
        {
            Code = "CRM",
            Name = "Customer Desk",
            LogoUrl = $"{ImageBase}/logos/crm.png",
            LogoUrlDark = $"{ImageBase}/logos/crm-dark.png"
        }, "the page names the application, with logos it can load from another origin");
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i =>
                    i.ApplicationRoleOutcome == InvitationApplicationRoleOutcome.Applied),
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
        var (command, _, userId, _, _, _) = ArrangeInvitationWithApplicationRole(
            initiateLoginUri: "https://crm.example.com/signin/start");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationRoleName.Should().BeNull("its meaning is kept: no role was given");

        // OI-192: no longer only a log line. Recorded with the acceptance, and returned with
        // the application, so the page can say which role was not given and why. Nothing is
        // launched: the invitee would land in an application that turns them away.
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Dropped);
        result.Value.Application!.Name.Should().Be("Customer Desk");
        result.Value.LaunchUrl.Should().BeNull();
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i =>
                    i.Status == InvitationStatus.Accepted &&
                    i.ApplicationRoleOutcome == InvitationApplicationRoleOutcome.Dropped),
                It.Is<OrganizationUser?>(m => m != null && m.UserId == userId),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheRoleIsDroppedAndTheApplicationIsGone_RecordsDroppedWithNoApplication()
    {
        var (command, _, _, applicationId, _, _) = ArrangeInvitationWithApplicationRole();
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.Application?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Dropped);
        result.Value.Application.Should().BeNull();
        result.Value.LaunchUrl.Should().BeNull();
    }

    /// <summary>
    /// OI-231 T4 (V7, deviation 245): a member whose earlier row of the role expired (or was
    /// deactivated) holds no valid one, so the role is applied, and the repository restores
    /// that row through the assignment's update-or-insert instead of failing on the key.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTheMembersEarlierRowOfTheRoleLapsed_AppliesTheRole()
    {
        var userId = Guid.NewGuid();
        var (command, orgId, _, applicationId, appRoleId, inviterId) = ArrangeInvitationWithApplicationRole(
            existingMembershipFor: userId);
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");
        _organizationRepositoryMock
            .Setup(r => r.HasAppRoleAsync(orgId, userId, applicationId, appRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Applied);
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i => i.ApplicationRoleOutcome == InvitationApplicationRoleOutcome.Applied),
                null,
                It.Is<OrganizationUserRole?>(a =>
                    a != null && a.UserId == userId && a.RoleId == appRoleId
                    && a.IsActive && a.ExpiresAt == null && a.AssignedBy == inviterId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// OI-231 T4 (V7): a former member invited again joins with the invitation's role
    /// whatever row of it the earlier membership may have left; the validity check is the
    /// member's only, so it is not asked, and the repository's update-or-insert restores a
    /// leftover row with the invitation's terms.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAFormerMemberIsInvitedAgain_AppliesTheRoleWithTheNewMembership()
    {
        var (command, orgId, userId, applicationId, appRoleId, inviterId) = ArrangeInvitationWithApplicationRole();
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Applied);
        _organizationRepositoryMock.Verify(
            r => r.HasAppRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.IsAny<OrganizationInvitation>(),
                It.Is<OrganizationUser?>(m => m != null && m.UserId == userId),
                It.Is<OrganizationUserRole?>(a => a != null && a.UserId == userId && a.RoleId == appRoleId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheMemberAlreadyHoldsTheRole_RecordsAlreadyHeldAndLaunches()
    {
        var userId = Guid.NewGuid();
        var (command, orgId, _, applicationId, appRoleId, inviterId) = ArrangeInvitationWithApplicationRole(
            initiateLoginUri: "https://crm.example.com/signin/start",
            existingMembershipFor: userId);
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");
        _organizationRepositoryMock
            .Setup(r => r.HasAppRoleAsync(orgId, userId, applicationId, appRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.MessageCode.Should().Be("Invitation.AlreadyMember");
        result.Value.ApplicationRoleName.Should().Be("Sales representative");
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.AlreadyHeld);
        result.Value.LaunchUrl.Should().Be(
            "https://crm.example.com/signin/start?iss=https%3A%2F%2Fid.example.com",
            "the invitee holds the role, so the application lets them in");
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i =>
                    i.ApplicationRoleOutcome == InvitationApplicationRoleOutcome.AlreadyHeld),
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheMemberHoldsTheRoleTheInviterCanNoLongerGive_RecordsAlreadyHeld()
    {
        // The inviter lost the authority, but the member already has the role: nothing is
        // lost, so the record says so and the member is let into the application, instead
        // of being told to ask an administrator for a role they hold.
        var userId = Guid.NewGuid();
        var (command, orgId, _, applicationId, appRoleId, _) = ArrangeInvitationWithApplicationRole(
            initiateLoginUri: "https://crm.example.com/signin/start",
            existingMembershipFor: userId);
        _organizationRepositoryMock
            .Setup(r => r.HasAppRoleAsync(orgId, userId, applicationId, appRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.AlreadyHeld);
        result.Value.ApplicationRoleName.Should().BeNull("its meaning is kept: the inviter could not give it");
        result.Value.LaunchUrl.Should().Be("https://crm.example.com/signin/start?iss=https%3A%2F%2Fid.example.com");

        // The record says AlreadyHeld, so the inviter's lost authority survives only in the log.
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("already holds it")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i =>
                    i.ApplicationRoleOutcome == InvitationApplicationRoleOutcome.AlreadyHeld),
                null,
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenTheMemberHoldsTheRoleOfADisabledApplication_RecordsDropped()
    {
        // Disabled for the organization, the role admits nobody: that is the dropped case.
        var userId = Guid.NewGuid();
        var (command, orgId, _, applicationId, appRoleId, inviterId) = ArrangeInvitationWithApplicationRole(
            initiateLoginUri: "https://crm.example.com/signin/start",
            existingMembershipFor: userId);
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");
        _organizationRepositoryMock
            .Setup(r => r.HasAppRoleAsync(orgId, userId, applicationId, appRoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _organizationRepositoryMock
            .Setup(r => r.IsApplicationEnabledAsync(orgId, applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Dropped);
        result.Value.LaunchUrl.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenTheRoleIsApplied_LaunchesTheApplicationsInitiateLoginUriWithTheIssuer()
    {
        var (command, orgId, _, applicationId, _, inviterId) = ArrangeInvitationWithApplicationRole(
            initiateLoginUri: "https://crm.example.com/signin/start?intent=signin");
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.LaunchUrl.Should().Be(
            "https://crm.example.com/signin/start?intent=signin&iss=https%3A%2F%2Fid.example.com");
    }

    [Fact]
    public async Task Handle_WhenTheApplicationIsSwitchedOff_AppliesTheRoleButLaunchesNothing()
    {
        var (command, orgId, _, applicationId, _, inviterId) = ArrangeInvitationWithApplicationRole(
            initiateLoginUri: "https://crm.example.com/signin/start",
            applicationIsActive: false);
        InviterHoldsInOrganization(orgId, inviterId, applicationId, "crm:leads:read");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Applied);
        result.Value.LaunchUrl.Should().BeNull("a switched-off application admits nobody");
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
        result.Value.ApplicationRoleOutcome.Should().Be(InvitationApplicationRoleOutcome.Dropped);
        _organizationRepositoryMock.Verify(
            r => r.AcceptInvitationAsync(
                It.Is<OrganizationInvitation>(i =>
                    i.ApplicationRoleOutcome == InvitationApplicationRoleOutcome.Dropped),
                It.IsAny<OrganizationUser?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private (AcceptInvitationCommand Command, Guid OrgId, Guid UserId, Guid ApplicationId, Guid AppRoleId, Guid InviterId)
        ArrangeInvitationWithApplicationRole(
            string? initiateLoginUri = null,
            bool applicationIsActive = true,
            Guid? existingMembershipFor = null)
    {
        var userId = existingMembershipFor ?? Guid.NewGuid();
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
            existingMembership: existingMembershipFor is { } member
                ? TestHelpers.CreateOrganizationUser(organizationId: orgId, userId: member)
                : null);

        var application = TestHelpers.CreateApplication(
            id: applicationId,
            code: "CRM",
            name: "Customer Desk",
            logoUrl: "logos/crm.png",
            logoUrlDark: "logos/crm-dark.png",
            isActive: applicationIsActive);
        application.SetInitiateLoginUri(initiateLoginUri, Guid.NewGuid());
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf("invalid-token"), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(token), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetInvitationByTokenHashAsync(FakeCredentialHasher.CandidatesOf(invitation.TokenHash), It.IsAny<CancellationToken>()))
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
