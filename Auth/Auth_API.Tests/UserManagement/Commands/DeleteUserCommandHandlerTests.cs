using Auth.Application.Features.Users.Common;
using Auth.Application.Features.Users.DeleteUser;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class DeleteUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ILogger<DeleteUserCommandHandler>> _loggerMock;
    private readonly DeleteUserCommandHandler _handler;

    public DeleteUserCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _credentialRevocationMock = new Mock<ICredentialRevocationService>();
        _publisherMock = new Mock<IPublisher>();
        _loggerMock = new Mock<ILogger<DeleteUserCommandHandler>>();

        // Default: the user owns no organizations
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());

        _handler = new DeleteUserCommandHandler(
            _userRepositoryMock.Object,
            new OwnedOrganizationDeletionGuard(
                _organizationRepositoryMock.Object,
                new Mock<ILogger<OwnedOrganizationDeletionGuard>>().Object),
            _credentialRevocationMock.Object,
            _publisherMock.Object,
            _loggerMock.Object);
    }

    private static Organization CreateOrganization(Guid ownerId, bool isAutoCreated) => new(
        id: Guid.NewGuid(),
        code: $"org-{Guid.NewGuid():N}",
        name: "Test Org",
        description: null,
        logoUrl: null,
        website: null,
        contactEmail: "org@test.com",
        ownerId: ownerId,
        isActive: true,
        isAutoCreated: isAutoCreated,
        createdAt: DateTime.UtcNow,
        createdBy: ownerId,
        modifiedAt: null,
        modifiedBy: null);

    [Fact]
    public async Task Handle_ValidUser_DeletesAndPublishesEvent()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once());
        _credentialRevocationMock.Verify(
            s => s.RevokeAllCredentialsAsync(userId, command.DeletedBy, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        var command = new DeleteUserCommand(Guid.NewGuid()) { DeletedBy = Guid.NewGuid() };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_SystemUser_ReturnsForbiddenError()
    {
        var userId = Guid.NewGuid();
        // Create a system user via constructor - need to check if there's a way
        // System user has IsSystemUser = true
        var user = new User(
            id: userId, email: "system@test.com", normalizedEmail: "SYSTEM@TEST.COM",
            passwordHash: "hash", firstName: "System", lastName: "User",
            displayName: null, phoneNumber: null,
            status: Auth.Domain.Enums.UserStatus.Active,
            emailConfirmed: true, phoneConfirmed: false,
            twoFactorEnabled: false, twoFactorSecret: null,
            failedLoginAttempts: 0, lockoutEnd: null, lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow, mustChangePassword: false,
            preferredLanguage: "en", timeZone: "UTC", metadata: null,
            isSystemUser: true,
            createdAt: DateTime.UtcNow, createdBy: Guid.NewGuid(),
            modifiedAt: null, modifiedBy: null);

        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_OwnsRealOrganizationWithOtherMembers_ReturnsConflictAndDoesNotDelete()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var org = CreateOrganization(userId, isAutoCreated: false);
        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { org });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [org.Id] = 3 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.CannotDeleteOrganizationOwner);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _organizationRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _credentialRevocationMock.Verify(
            s => s.RevokeAllCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_PersonalOrganizationWithOtherMembers_ReturnsConflictAndDoesNotDelete()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var personalOrg = CreateOrganization(userId, isAutoCreated: true);
        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { personalOrg });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(personalOrg.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [personalOrg.Id] = 3 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.CannotDeletePersonalOrganizationWithMembers);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _organizationRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_PersonalOrganizationSoleMember_DeletesOrgAndAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var personalOrg = CreateOrganization(userId, isAutoCreated: true);
        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { personalOrg });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [personalOrg.Id] = 1 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _organizationRepositoryMock.Verify(r => r.DeleteAsync(personalOrg.Id, It.IsAny<CancellationToken>()), Times.Once());
        _userRepositoryMock.Verify(r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_OwnsRealOrganizationSoleMember_DeletesOrgAndAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var org = CreateOrganization(userId, isAutoCreated: false);
        var command = new DeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { org });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [org.Id] = 1 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _organizationRepositoryMock.Verify(r => r.DeleteAsync(org.Id, It.IsAny<CancellationToken>()), Times.Once());
        _userRepositoryMock.Verify(r => r.DeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<UserDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}
