using Auth.Application.Features.Users.ActivateAccount;
using Auth.Application.Features.Users.DeactivateAccount;
using Auth.Application.Features.Users.LockAccount;
using Auth.Application.Features.Users.UnlockAccount;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.UserManagement.Commands;

/// <summary>
/// The four administrator status actions write the lock columns through one
/// statement each (<see cref="IUserRepository.SetAccountStatusAsync"/>), with the
/// change the account decided, and never through <see cref="IUserRepository.UpdateAsync"/>,
/// whose entity was read before the action and may carry a stale lock (X12, LK1.4).
/// </summary>
internal static class AccountStatusWrites
{
    public static void VerifyWrittenOnlyAs(Mock<IUserRepository> users, Guid userId, Guid actor, AccountStatusChange expected)
    {
        users.Verify(r => r.SetAccountStatusAsync(userId, expected, actor, It.IsAny<CancellationToken>()), Times.Once());
        users.Verify(r => r.SetAccountStatusAsync(It.IsAny<Guid>(), It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once());
        users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// The same, for the two actions that can take the last full administrator away:
    /// written through the locked change, committed, and never through the
    /// repository's own unlocked statement.
    /// </summary>
    public static void VerifyWrittenOnlyAs(
        Mock<IAccountAuthorityChange> change, Mock<IUserRepository> users, Guid userId, Guid actor, AccountStatusChange expected)
    {
        change.Verify(c => c.SetAccountStatusAsync(userId, expected, actor, It.IsAny<CancellationToken>()), Times.Once());
        change.Verify(c => c.SetAccountStatusAsync(It.IsAny<Guid>(), It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once());
        change.Verify(c => c.Commit(), Times.Once());
        users.Verify(r => r.SetAccountStatusAsync(It.IsAny<Guid>(), It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        users.Verify(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}

public class ActivateAccountCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly ActivateAccountCommandHandler _handler;

    public ActivateAccountCommandHandlerTests()
    {
        _handler = new ActivateAccountCommandHandler(
            _userRepositoryMock.Object,
            AccountRules.Policy(),
            new Mock<ILogger<ActivateAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_ActivatesAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Inactive);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var activatedBy = Guid.NewGuid();
        var result = await _handler.Handle(new ActivateAccountCommand(userId, activatedBy), CancellationToken.None);

        result.IsError.Should().BeFalse();
        AccountStatusWrites.VerifyWrittenOnlyAs(_userRepositoryMock, userId, activatedBy, AccountStatusChange.Activate());
    }

    [Fact]
    public async Task Handle_HardLockedUser_ActivationClearsTheLockAndTheCount()
    {
        // An administrator's activation is one of the exits of the hard lock
        // (LK4 (e)): active, a count of 0, no expiry.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Locked, failedLoginAttempts: 100);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        AccountStatusChange? written = null;
        _userRepositoryMock
            .Setup(r => r.SetAccountStatusAsync(userId, It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, AccountStatusChange, Guid, CancellationToken>((_, change, _, _) => written = change)
            .Returns(Task.CompletedTask);

        await _handler.Handle(new ActivateAccountCommand(userId, Guid.NewGuid()), CancellationToken.None);

        written.Should().NotBeNull();
        written!.Status.Should().Be(UserStatus.Active);
        written.ResetsLock.Should().BeTrue("activation zeroes the count, so the account is not one wrong password from the hard limit");
        written.LockoutEndUtc.Should().BeNull();
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
    private readonly Mock<IAccountAuthorityChange> _change;
    private readonly DeactivateAccountCommandHandler _handler;

    public DeactivateAccountCommandHandlerTests()
    {
        _change = _userRepositoryMock.SetupAuthorityChange();
        _handler = new DeactivateAccountCommandHandler(
            _userRepositoryMock.Object,
            AccountRules.Policy(users: _userRepositoryMock),
            _credentialRevocationMock.Object,
            new Mock<ILogger<DeactivateAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_DeactivatesAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Active);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var deactivatedBy = Guid.NewGuid();
        var result = await _handler.Handle(new DeactivateAccountCommand(userId, deactivatedBy), CancellationToken.None);

        result.IsError.Should().BeFalse();
        // The status only: the count and the expiry are left as they are.
        AccountStatusWrites.VerifyWrittenOnlyAs(_change, _userRepositoryMock, userId, deactivatedBy, AccountStatusChange.Deactivate());
        AccountStatusChange.Deactivate().ResetsLock.Should().BeFalse();
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
    private readonly Mock<IAccountAuthorityChange> _change;
    private readonly LockAccountCommandHandler _handler;

    public LockAccountCommandHandlerTests()
    {
        _change = _userRepositoryMock.SetupAuthorityChange();
        _handler = new LockAccountCommandHandler(
            _userRepositoryMock.Object,
            AccountRules.Policy(users: _userRepositoryMock),
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
        var change = AccountStatusChange.Lock(user.LockoutEnd);
        user.LockoutEnd.Should().NotBeNull("a timed administrator's lock keeps its expiry");
        AccountStatusWrites.VerifyWrittenOnlyAs(_change, _userRepositoryMock, userId, lockedBy, change);
        change.ResetsLock.Should().BeTrue("an administrator's lock starts at a count of 0, which is what marks it administrative");

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
            AccountRules.Policy(),
            _eventDispatcherMock.Object,
            new Mock<ILogger<UnlockAccountCommandHandler>>().Object);
    }

    [Fact]
    public async Task Handle_ValidUser_UnlocksAccount()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, status: UserStatus.Locked);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var unlockedBy = Guid.NewGuid();
        var result = await _handler.Handle(new UnlockAccountCommand(userId, unlockedBy), CancellationToken.None);

        result.IsError.Should().BeFalse();
        AccountStatusWrites.VerifyWrittenOnlyAs(_userRepositoryMock, userId, unlockedBy, AccountStatusChange.Unlock());
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        _userRepositoryMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

        var result = await _handler.Handle(new UnlockAccountCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }
}
