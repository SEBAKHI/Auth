using Auth.Application.Features.Users.Common;
using Auth.Application.Features.Users.HardDeleteUser;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class HardDeleteUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly HardDeleteUserCommandHandler _handler;

    public HardDeleteUserCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _credentialRevocationMock = new Mock<ICredentialRevocationService>();
        _publisherMock = new Mock<IPublisher>();

        // Default: the user owns no organizations and the purge succeeds
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization>());
        _userRepositoryMock
            .Setup(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _handler = new HardDeleteUserCommandHandler(
            _userRepositoryMock.Object,
            new OwnedOrganizationDeletionGuard(
                _organizationRepositoryMock.Object,
                new Mock<ILogger<OwnedOrganizationDeletionGuard>>().Object),
            _credentialRevocationMock.Object,
            _publisherMock.Object,
            new Mock<ILogger<HardDeleteUserCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_SoftDeletedUser_PurgesAndPublishesEvent()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, isDeleted: true, deletedAt: DateTime.UtcNow);
        var command = new HardDeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once());
        _credentialRevocationMock.Verify(
            s => s.RevokeAllCredentialsAsync(userId, command.DeletedBy, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(p => p.Publish(It.IsAny<UserHardDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        var command = new HardDeleteUserCommand(Guid.NewGuid()) { DeletedBy = Guid.NewGuid() };
        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(command.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.NotFound");
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_LiveUser_ReturnsNotSoftDeletedError()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, isDeleted: false);
        var command = new HardDeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.NotSoftDeleted);
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _publisherMock.Verify(p => p.Publish(It.IsAny<UserHardDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task SoftDelete_SystemAccountId_IsRefusedEvenWithoutTheFlag()
    {
        // The Users table has no IsSystemUser column, so the flag arrives false
        // from every query — the well-known id must carry the guard alone.
        var organizationRepository = new Mock<IOrganizationRepository>();
        var softDeleteHandler = new Auth.Application.Features.Users.DeleteUser.DeleteUserCommandHandler(
            _userRepositoryMock.Object,
            new OwnedOrganizationDeletionGuard(
                organizationRepository.Object,
                new Mock<ILogger<OwnedOrganizationDeletionGuard>>().Object),
            new Mock<ICredentialRevocationService>().Object,
            _publisherMock.Object,
            new Mock<ILogger<Auth.Application.Features.Users.DeleteUser.DeleteUserCommandHandler>>().Object);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(WellKnownUserIds.System, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: WellKnownUserIds.System, isSystemUser: false));

        var result = await softDeleteHandler.Handle(
            new Auth.Application.Features.Users.DeleteUser.DeleteUserCommand(WellKnownUserIds.System)
            {
                DeletedBy = Guid.NewGuid()
            },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.CannotDeleteSystemUser);
        _userRepositoryMock.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_SystemAccountId_IsRefusedWithoutRepositoryLookup()
    {
        var command = new HardDeleteUserCommand(WellKnownUserIds.System) { DeletedBy = Guid.NewGuid() };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.CannotDeleteSystemUser);
        _userRepositoryMock.Verify(
            r => r.GetByIdIncludeDeletedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_SystemUserFlag_ReturnsForbiddenError()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, isSystemUser: true, isDeleted: true, deletedAt: DateTime.UtcNow);
        var command = new HardDeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.CannotDeleteSystemUser);
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OwnedOrganizationWithOtherMembers_Blocks()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, isDeleted: true, deletedAt: DateTime.UtcNow);
        var organization = TestHelpers.CreateOrganization(ownerId: userId);
        var command = new HardDeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { organization });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [organization.Id] = 2 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.CannotDeleteOrganizationOwner);
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_SoleMemberOwnedOrganization_IsDeletedWithTheAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, isDeleted: true, deletedAt: DateTime.UtcNow);
        var organization = TestHelpers.CreateOrganization(ownerId: userId);
        var command = new HardDeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _organizationRepositoryMock
            .Setup(r => r.GetByOwnerAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Organization> { organization });
        _organizationRepositoryMock
            .Setup(r => r.GetMemberCountsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [organization.Id] = 1 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _organizationRepositoryMock.Verify(r => r.DeleteAsync(organization.Id, It.IsAny<CancellationToken>()), Times.Once());
        _userRepositoryMock.Verify(r => r.HardDeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_PurgeLosesConcurrencyRace_ReturnsNotSoftDeleted()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, isDeleted: true, deletedAt: DateTime.UtcNow);
        var command = new HardDeleteUserCommand(userId) { DeletedBy = Guid.NewGuid() };

        _userRepositoryMock
            .Setup(r => r.GetByIdIncludeDeletedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(r => r.HardDeleteAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.NotSoftDeleted);
        _publisherMock.Verify(p => p.Publish(It.IsAny<UserHardDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}
