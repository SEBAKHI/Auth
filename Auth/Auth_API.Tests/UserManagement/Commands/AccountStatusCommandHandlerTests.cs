using Auth.Application.Features.Users.ActivateAccount;
using Auth.Application.Features.Users.DeactivateAccount;
using Auth.Application.Features.Users.LockAccount;
using Auth.Application.Features.Users.UnlockAccount;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

public class ActivateAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly ActivateAccountCommandHandler _handler;

    public ActivateAccountCommandHandlerTests()
    {
        _handler = new ActivateAccountCommandHandler(
            _userRepositoryMock.Object,
            new Mock<ILogger<ActivateAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_ActivatesAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Inactive);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(new ActivateAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new ActivateAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }
}

public class DeactivateAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly DeactivateAccountCommandHandler _handler;

    public DeactivateAccountCommandHandlerTests()
    {
        _handler = new DeactivateAccountCommandHandler(
            _userRepositoryMock.Object,
            _credentialRevocationMock.Object,
            new Mock<ILogger<DeactivateAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_DeactivatesAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Active);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(new DeactivateAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidUser_RevokesEveryCredential()
    {
        var userId = Guid.NewGuid();
        var deactivatedBy = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Active);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        await _handler.Handle(new DeactivateAccountCommand(userId, deactivatedBy), CancellationToken.None);

        // Setting Status alone offboards nobody: no credential-renewal path reads
        // it, so a held refresh token keeps minting fully authorized access
        // tokens for as long as it is rotated.
        _credentialRevocationMock.Verify(
            c => c.RevokeAllCredentialsAsync(userId, deactivatedBy, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Theory]
    [InlineData(typeof(DeactivateAccountCommandHandler))]
    [InlineData(typeof(LockAccountCommandHandler))]
    public void OffboardingHandlers_CannotReachTheSessionTableDirectly(Type handler)
    {
        // Verifying "TerminateAllForUserAsync was never called" on these handlers
        // would prove nothing, because they no longer take the repository that
        // exposes it — the assertion would hold for any implementation at all.
        // Depending on it again is the actual regression, so that is what this
        // asserts.
        //
        // Why it must not come back: the revocation service lists the active
        // sessions before ending them, so that it can blacklist each session id.
        // A direct termination running first stamps EndedAt on every row, the
        // listing comes back empty, nothing is blacklisted, and the caller is
        // told it succeeded. The revocation replaces that call; it cannot
        // accompany it.
        handler.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Should().NotContain(parameter => parameter.ParameterType == typeof(IUserSessionRepository),
                $"{handler.Name} must offboard through ICredentialRevocationService only");
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new DeactivateAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class LockAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly LockAccountCommandHandler _handler;

    public LockAccountCommandHandlerTests()
    {
        _handler = new LockAccountCommandHandler(
            _userRepositoryMock.Object,
            _credentialRevocationMock.Object,
            _eventDispatcherMock.Object,
            new Mock<ILogger<LockAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_LocksAccountAndRevokesEveryCredential()
    {
        var userId = Guid.NewGuid();
        var lockedBy = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new LockAccountCommand(userId, "Suspicious activity", 30, lockedBy);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();

        // An administrator locking an account is usually answering an incident,
        // so the access tokens already out there are the point of the action.
        _credentialRevocationMock.Verify(
            c => c.RevokeAllCredentialsAsync(userId, lockedBy, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_IndefiniteLock_StillRevokesEveryCredential()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        // A lock with no duration is the strongest form of the action, so it is
        // the one that must not quietly do less than the timed one.
        await _handler.Handle(
            new LockAccountCommand(userId, "Suspicious activity", null, Guid.NewGuid()),
            CancellationToken.None);

        _credentialRevocationMock.Verify(
            c => c.RevokeAllCredentialsAsync(userId, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(
            new LockAccountCommand(Guid.NewGuid(), "Test", null, Guid.NewGuid()),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}

public class UnlockAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock = new();
    private readonly UnlockAccountCommandHandler _handler;

    public UnlockAccountCommandHandlerTests()
    {
        _handler = new UnlockAccountCommandHandler(
            _userRepositoryMock.Object,
            _eventDispatcherMock.Object,
            new Mock<ILogger<UnlockAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_UnlocksAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Locked);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await _handler.Handle(new UnlockAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new UnlockAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
