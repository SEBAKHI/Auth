using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for LoginCommandHandler.
/// </summary>
public class LoginCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepositoryMock;
    private readonly Mock<IAccountDeletionRequestRepository> _accountDeletionRequestRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock;
    private readonly Mock<ITwoFactorChallengeService> _twoFactorChallengeServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<LoginCommandHandler>> _loggerMock;
    private readonly PasswordSettings _passwordSettings;
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _loginAttemptRepositoryMock = new Mock<ILoginAttemptRepository>();
        _accountDeletionRequestRepositoryMock = new Mock<IAccountDeletionRequestRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _loginResponseBuilderMock = new Mock<ILoginResponseBuilder>();
        _twoFactorChallengeServiceMock = new Mock<ITwoFactorChallengeService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<LoginCommandHandler>>();

        _passwordSettings = TestHelpers.CreatePasswordSettings();

        _handler = new LoginCommandHandler(
            _userRepositoryMock.Object,
            _loginAttemptRepositoryMock.Object,
            _accountDeletionRequestRepositoryMock.Object,
            _passwordHasherMock.Object,
            _loginResponseBuilderMock.Object,
            _twoFactorChallengeServiceMock.Object,
            _eventDispatcherMock.Object,
            TestHelpers.CreateOptions(_passwordSettings),
            _loggerMock.Object);
    }

    private static LoginCommand CreateCommand(
        string email = "test@example.com",
        string password = "ValidPass1!",
        string? ipAddress = "127.0.0.1",
        string? userAgent = "TestAgent/1.0",
        string? deviceId = null)
        => new(email, password, ipAddress, userAgent, deviceId);

    private LoginResponse CreateLoginResponse() => new()
    {
        Token = new TokenResponse
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresIn = 900,
            RefreshExpiresIn = 604800
        },
        User = new UserInfo
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        }
    };

    [Fact]
    public async Task Handle_ValidCredentials_ReturnsLoginResponse()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email);
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(loginResponse);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsInvalidCredentialsAndRecordsAttempt()
    {
        // Arrange
        var command = CreateCommand(email: "unknown@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidCredentials");

        VerifyFailureRecorded("User not found", Times.Once());
    }

    [Fact]
    public async Task Handle_InactiveAccount_ReturnsAccountInactiveError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(status: UserStatus.Inactive);
        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_PendingAccount_ReturnsAccountPendingError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(status: UserStatus.Pending);
        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountPending");
    }

    [Fact]
    public async Task Handle_LockedAccount_ReturnsAccountLockedError()
    {
        // Arrange
        var user = new User(
            id: Guid.NewGuid(),
            email: "locked@example.com",
            normalizedEmail: "LOCKED@EXAMPLE.COM",
            passwordHash: "TestPasswordHash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Locked,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(15),
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        // User has a lockoutEnd timestamp, so the time-bounded error code is expected.
        result.FirstError.Code.Should().Be("User.AccountLockedUntil");
    }

    [Fact]
    public async Task Handle_ExpiredLockout_UnlocksAndContinuesLogin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "locked-expired@example.com";

        // User with Locked status but expired lockout (lockoutEnd in the past)
        var lockedUser = new User(
            id: userId,
            email: email,
            normalizedEmail: email.ToUpperInvariant(),
            passwordHash: "TestPasswordHash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Locked,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(-1), // expired lockout
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var unlockedUser = TestHelpers.CreateUser(id: userId, email: email);
        var command = CreateCommand(email: email);
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .SetupSequence(r => r.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser)
            .ReturnsAsync(unlockedUser);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, unlockedUser.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(unlockedUser.PasswordHash!))
            .Returns(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(unlockedUser, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _userRepositoryMock.Verify(
            r => r.UnlockAsync(userId, userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LockedAccount_FamiliarSource_SignsInAndClearsTheLock()
    {
        // Arrange — locked by strangers' wrong passwords (timed, counter at the
        // threshold), but this device has signed in before: the lock is for them
        var lockedUser = new User(
            id: Guid.NewGuid(),
            email: "owner@example.com",
            normalizedEmail: "OWNER@EXAMPLE.COM",
            passwordHash: "TestPasswordHash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Locked,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(15),
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var unlockedUser = TestHelpers.CreateUser(id: lockedUser.Id, email: lockedUser.Email);
        var command = CreateCommand(email: lockedUser.Email, deviceId: "known-device");
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .SetupSequence(r => r.GetByEmailAsync(lockedUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser)
            .ReturnsAsync(unlockedUser);
        _loginAttemptRepositoryMock
            .Setup(r => r.HasSucceededFromAsync(
                lockedUser.Id, command.IpAddress, command.DeviceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, lockedUser.PasswordHash!))
            .Returns(true);
        _passwordHasherMock
            .Setup(h => h.NeedsRehash(unlockedUser.PasswordHash!))
            .Returns(false);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(unlockedUser, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — the owner gets in, and the account is theirs again: the lock
        // is cleared in full, not left as a Status=Locked row that credential
        // renewal would read as indefinite
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(
            r => r.UnlockAsync(lockedUser.Id, lockedUser.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_AdministrativeLock_FamiliarSource_IsStillRefused()
    {
        // Arrange — locked by an administrator: no expiry, counter untouched.
        // Familiarity must not open that door; it is an incident-response decision.
        var adminLocked = new User(
            id: Guid.NewGuid(),
            email: "incident@example.com",
            normalizedEmail: "INCIDENT@EXAMPLE.COM",
            passwordHash: "TestPasswordHash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Locked,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 0,
            lockoutEnd: null,
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var command = CreateCommand(email: adminLocked.Email, deviceId: "known-device");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(adminLocked.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminLocked);
        _loginAttemptRepositoryMock
            .Setup(r => r.HasSucceededFromAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountLocked");
        _passwordHasherMock.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _userRepositoryMock.Verify(
            r => r.UnlockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_LockedAccount_UnfamiliarSource_IsRefusedAndRecorded()
    {
        // Arrange — the stranger's own request against the lock they raised
        var lockedUser = new User(
            id: Guid.NewGuid(),
            email: "target@example.com",
            normalizedEmail: "TARGET@EXAMPLE.COM",
            passwordHash: "TestPasswordHash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Locked,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(15),
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var command = CreateCommand(email: lockedUser.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(lockedUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser);
        _loginAttemptRepositoryMock
            .Setup(r => r.HasSucceededFromAsync(
                lockedUser.Id, command.IpAddress, command.DeviceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountLockedUntil");
        _loginAttemptRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<LoginAttempt>(a => a.FailureReason == Auth.Domain.Constants.LoginFailureReasons.AccountLocked),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _passwordHasherMock.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoClientAddress_SkipsThePerSourceCeiling()
    {
        // Arrange — nothing to attribute the attempt to, so nothing extra to refuse
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email, ipAddress: null);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);
        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLoginResponse());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _loginAttemptRepositoryMock.Verify(
            r => r.CountFailedAttemptsForUserFromIpAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TooManyFailuresFromThisSource_RefusesBeforeVerifyingThePassword()
    {
        // Arrange — an active account, but this address has already spent its allowance
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _loginAttemptRepositoryMock
            .Setup(r => r.CountFailedAttemptsForUserFromIpAsync(
                user.Id, command.IpAddress!, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — refused as locked (the generic, undated answer: the address is
        // refused, not the account) and no Argon2 work was spent on it
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountLocked");
        _passwordHasherMock.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _userRepositoryMock.Verify(
            r => r.RecordFailedLoginAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_LockedAccount_FamiliarSource_WrongPassword_KeepsTheLock()
    {
        // Arrange — familiarity opens the door to password verification, not past it
        var lockedUser = CreateAutomaticallyLockedUser("owner-typo@example.com");
        var command = CreateCommand(email: lockedUser.Email, deviceId: "known-device");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(lockedUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser);
        _loginAttemptRepositoryMock
            .Setup(r => r.HasSucceededFromAsync(
                lockedUser.Id, command.IpAddress, command.DeviceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, lockedUser.PasswordHash!))
            .Returns(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidCredentials");
        _userRepositoryMock.Verify(
            r => r.RecordFailedLoginAsync(lockedUser.Id, It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _userRepositoryMock.Verify(
            r => r.UnlockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_LockedAccount_FamiliarSource_WithTwoFactor_UnlocksBeforeTheChallenge()
    {
        // Arrange — the unlock must precede the challenge, or the verify step
        // (which re-reads the row and refuses a live lock) fails the owner's code
        var lockedUser = CreateAutomaticallyLockedUser("owner-2fa@example.com");
        var unlockedUser = TestHelpers.CreateUser(id: lockedUser.Id, email: lockedUser.Email, twoFactorEnabled: true);
        var command = CreateCommand(email: lockedUser.Email, deviceId: "known-device");
        var order = new List<string>();

        _userRepositoryMock
            .SetupSequence(r => r.GetByEmailAsync(lockedUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockedUser)
            .ReturnsAsync(unlockedUser);
        _userRepositoryMock
            .Setup(r => r.UnlockAsync(lockedUser.Id, lockedUser.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("unlock"))
            .Returns(Task.CompletedTask);
        _loginAttemptRepositoryMock
            .Setup(r => r.HasSucceededFromAsync(
                lockedUser.Id, command.IpAddress, command.DeviceId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, lockedUser.PasswordHash!))
            .Returns(true);
        _passwordHasherMock
            .Setup(h => h.NeedsRehash(unlockedUser.PasswordHash!))
            .Returns(false);
        _twoFactorChallengeServiceMock
            .Setup(s => s.CreateChallengeAsync(
                It.IsAny<User>(), command.IpAddress, command.UserAgent, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("challenge"))
            .ReturnsAsync("challenge-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RequiresTwoFactor.Should().BeTrue();
        order.Should().Equal("unlock", "challenge");
    }

    [Fact]
    public async Task Handle_TimedAdministrativeLock_FamiliarSource_IsStillRefused()
    {
        // Arrange — an administrator locked the account for a day while it was
        // being brute-forced. Lock() zeroed the counter, so the timed expiry alone
        // must not make it read as automatic.
        var adminLocked = new User(
            id: Guid.NewGuid(),
            email: "incident-timed@example.com",
            normalizedEmail: "INCIDENT-TIMED@EXAMPLE.COM",
            passwordHash: "TestPasswordHash",
            firstName: "Test",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Locked,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 0,
            lockoutEnd: DateTime.UtcNow.AddHours(24),
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var command = CreateCommand(email: adminLocked.Email, deviceId: "known-device");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(adminLocked.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminLocked);
        _loginAttemptRepositoryMock
            .Setup(r => r.HasSucceededFromAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountLockedUntil");
        _passwordHasherMock.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _userRepositoryMock.Verify(
            r => r.UnlockAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static User CreateAutomaticallyLockedUser(string email) => new(
        id: Guid.NewGuid(),
        email: email,
        normalizedEmail: email.ToUpperInvariant(),
        passwordHash: "TestPasswordHash",
        firstName: "Test",
        lastName: "User",
        displayName: null,
        phoneNumber: null,
        status: UserStatus.Locked,
        emailConfirmed: true,
        phoneConfirmed: false,
        twoFactorEnabled: false,
        twoFactorSecret: null,
        failedLoginAttempts: 5,
        lockoutEnd: DateTime.UtcNow.AddMinutes(15),
        lastLoginAt: null,
        passwordChangedAt: DateTime.UtcNow,
        mustChangePassword: false,
        preferredLanguage: "en",
        timeZone: "UTC",
        metadata: null,
        isSystemUser: false,
        createdAt: DateTime.UtcNow,
        createdBy: Guid.NewGuid(),
        modifiedAt: null,
        modifiedBy: null);

    [Fact]
    public async Task Handle_ExternalOnlyUser_NullPasswordHash_ReturnsInvalidCredentials()
    {
        // Arrange
        var user = new User(
            id: Guid.NewGuid(),
            email: "external@example.com",
            normalizedEmail: "EXTERNAL@EXAMPLE.COM",
            passwordHash: null!,
            firstName: "External",
            lastName: "User",
            displayName: null,
            phoneNumber: null,
            status: UserStatus.Active,
            emailConfirmed: true,
            phoneConfirmed: false,
            twoFactorEnabled: false,
            twoFactorSecret: null,
            failedLoginAttempts: 0,
            lockoutEnd: null,
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: false,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: Guid.NewGuid(),
            modifiedAt: null,
            modifiedBy: null);

        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidCredentials");
    }

    [Fact]
    public async Task Handle_WrongPassword_RecordsFailedLoginAndReturnsInvalidCredentials()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email, password: "WrongPassword!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("WrongPassword!", user.PasswordHash!))
            .Returns(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidCredentials");

        _userRepositoryMock.Verify(
            r => r.RecordFailedLoginAsync(
                user.Id,
                _passwordSettings.MaxFailedAttempts,
                _passwordSettings.LockoutDuration,
                It.IsAny<CancellationToken>()),
            Times.Once);

        VerifyFailureRecorded("Invalid password", Times.Once());
    }

    [Fact]
    public async Task Handle_UnconfirmedEmail_ReturnsEmailNotConfirmedError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(emailConfirmed: false);
        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.EmailNotConfirmed");
    }

    [Fact]
    public async Task Handle_NeedsRehash_RehashesPasswordAndContinues()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email);
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.HashPassword(command.Password))
            .Returns("NewRehashValue");

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _userRepositoryMock.Verify(
            r => r.UpdatePasswordAsync(user.Id, "NewRehashValue", user.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidLogin_CallsRecordSuccessfulLoginOnEntity()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email, ipAddress: "10.0.0.1", userAgent: "Chrome/120");
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        user.LastLoginAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidLogin_DispatchesDomainEvents()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email);
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidLogin_PassesTheUserAgentAndDeviceIdSeparately()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email, userAgent: "Chrome/120", deviceId: "device-123");
        var loginResponse = CreateLoginResponse();

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                user,
                command.IpAddress,
                // Two arguments, not one concatenated string: the combined form
                // used to overflow the UserAgent column and cost the session row.
                "Chrome/120",
                "device-123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(
                user,
                command.IpAddress,
                // Two arguments, not one concatenated string: the combined form
                // used to overflow the UserAgent column and cost the session row.
                "Chrome/120",
                "device-123",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CancellationTokenPropagated_PassesToDependencies()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email);
        var loginResponse = CreateLoginResponse();
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, token))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), token))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(command, token);

        // Assert
        result.IsError.Should().BeFalse();

        _userRepositoryMock.Verify(r => r.GetByEmailAsync(user.Email, token), Times.Once);
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(user, command.IpAddress, It.IsAny<string?>(), It.IsAny<string?>(), token),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, token),
            Times.Once);
    }

    [Fact]
    public async Task Handle_TwoFactorEnabled_ReturnsChallengeWithoutTokens()
    {
        // Arrange
        var user = TestHelpers.CreateUser(twoFactorEnabled: true);
        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _twoFactorChallengeServiceMock
            .Setup(s => s.CreateChallengeAsync(
                user, command.IpAddress, command.UserAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync("challenge-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.RequiresTwoFactor.Should().BeTrue();
        result.Value.TwoFactorChallengeToken.Should().Be("challenge-token");
        result.Value.Token.Should().BeNull();
        result.Value.User.Should().BeNull();
    }

    /// <summary>
    /// Asserts the exact row this handler wrote, not merely that it wrote one.
    /// The loose <c>It.IsAny&lt;LoginAttempt&gt;()</c> form this replaces is how the
    /// two-factor gate shipped for months recording a success as a failure: the
    /// assertion passed for any outcome and any reason.
    /// </summary>
    private void VerifyFailureRecorded(string expectedReason, Times times) =>
        _loginAttemptRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<LoginAttempt>(a =>
                    !a.IsSuccess &&
                    a.FailureReason == expectedReason &&
                    a.TwoFactorChallengeId == null),
                It.IsAny<CancellationToken>()),
            times);

    [Fact]
    public async Task Handle_TwoFactorEnabled_RecordsNoAttemptOfItsOwn()
    {
        // Arrange
        var user = TestHelpers.CreateUser(twoFactorEnabled: true);
        var command = CreateCommand(email: user.Email);

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Password, user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.NeedsRehash(user.PasswordHash!))
            .Returns(false);

        _twoFactorChallengeServiceMock
            .Setup(s => s.CreateChallengeAsync(
                user, command.IpAddress, command.UserAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync("challenge-token");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        user.LastLoginAt.Should().BeNull();

        // The ceremony's row belongs to the challenge service, which opens it as
        // part of issuing the challenge. This handler writing one too is exactly
        // the defect: it produced a second row, marked failed, on every clean
        // two-factor sign-in.
        _loginAttemptRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _twoFactorChallengeServiceMock.Verify(
            s => s.CreateChallengeAsync(
                user, command.IpAddress, command.UserAgent, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UserNotFound_DoesNotCallPasswordHasher()
    {
        // Arrange
        var command = CreateCommand(email: "notfound@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordHasherMock.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WrongPassword_DoesNotBuildLoginResponse()
    {
        // Arrange
        var user = TestHelpers.CreateUser();
        var command = CreateCommand(email: user.Email, password: "WrongPass!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("WrongPass!", user.PasswordHash!))
            .Returns(false);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
