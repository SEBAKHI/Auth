using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for ResetPasswordCommandHandler.
/// </summary>
public class ResetPasswordCommandHandlerTests
{
    private const string ValidToken = "valid-token";
    private const string ValidTokenHash = $"hmac({ValidToken})";

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordResetTokenRepository> _passwordResetTokenRepositoryMock;
    private readonly Mock<IPasswordHistoryRepository> _passwordHistoryRepositoryMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly List<IDomainEvent> _dispatchedEvents = [];
    private readonly Mock<ILogger<ResetPasswordCommandHandler>> _loggerMock;
    private readonly PasswordSettings _passwordSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly PasswordValidator _passwordValidator;
    private readonly ResetPasswordCommandHandler _handler;

    public ResetPasswordCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordResetTokenRepositoryMock = new Mock<IPasswordResetTokenRepository>();
        _passwordHistoryRepositoryMock = new Mock<IPasswordHistoryRepository>();
        _credentialRevocationMock = new Mock<ICredentialRevocationService>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _tokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<ResetPasswordCommandHandler>>();

        _tokenKeyServiceMock.Setup(k => k.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string token) => $"hmac({token})");

        // Mirrors the real dispatcher: drain, then CLEAR. Capturing at publish time is what
        // makes the assertions meaningful - reading DomainEvents after the call would show
        // whatever a production dispatcher had already emptied.
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Callback<AggregateRoot, CancellationToken>((aggregate, _) =>
            {
                _dispatchedEvents.AddRange(aggregate.DomainEvents);
                aggregate.ClearDomainEvents();
            })
            .Returns(Task.CompletedTask);

        _passwordSettings = TestHelpers.CreatePasswordSettings();
        _sessionSettings = TestHelpers.CreateSessionSettings();

        _passwordValidator = new PasswordValidator(
            TestHelpers.CreateOptions(_passwordSettings));

        _handler = new ResetPasswordCommandHandler(
            _userRepositoryMock.Object,
            _passwordResetTokenRepositoryMock.Object,
            _passwordHistoryRepositoryMock.Object,
            _credentialRevocationMock.Object,
            _passwordHasherMock.Object,
            _tokenKeyServiceMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidReset_UpdatesPasswordAndReturnsSuccess()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdatePasswordAsync(
            user.Id, "NewHashedPassword", user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidReset_LooksTokenUpByItsDeterministicHash()
    {
        // Arrange - the token alone identifies the row; no email is involved.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.GetByTokenHashAsync(
            ValidTokenHash, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.GetByEmailAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExternalOnlyUserWithoutPassword_SetsFirstPasswordWithoutHistoryEntry()
    {
        // Arrange - an external-only account uses the reset link to set its
        // FIRST password; there is no previous hash to compare or archive.
        var user = TestHelpers.CreateUser(email: "external@example.com", passwordHash: null);
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdatePasswordAsync(
            user.Id, "NewHashedPassword", user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _passwordHistoryRepositoryMock.Verify(r => r.AddAsync(
            It.IsAny<PasswordHistory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownToken_ReturnsInvalidOrExpiredTokenError()
    {
        // Arrange - GetByTokenHashAsync also filters out used and expired tokens,
        // so an unknown, spent or stale token all land here.
        var command = new ResetPasswordCommand("wrong-token", "NewPass1!");

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordResetToken?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PasswordReset.InvalidOrExpiredToken");
    }

    [Fact]
    public async Task Handle_TokenResolvesToMissingUser_ReturnsInvalidOrExpiredTokenError()
    {
        // Arrange
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: Guid.NewGuid(), tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(ValidTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(resetToken.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PasswordReset.InvalidOrExpiredToken");
    }

    [Fact]
    public async Task Handle_WeakNewPassword_ReturnsValidationErrors()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "weak");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.StartsWith("Password."));
    }

    [Fact]
    public async Task Handle_ReusedPassword_ReturnsPasswordRecentlyUsedError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(user.Id, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "OldHash" });

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", "OldHash"))
            .Returns(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordRecentlyUsed");
    }

    [Fact]
    public async Task Handle_SameAsCurrentPassword_ReturnsPasswordRecentlyUsedError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", user.PasswordHash!))
            .Returns(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.PasswordRecentlyUsed");
    }

    [Fact]
    public async Task Handle_ValidReset_MarksTokenAsUsed()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.MarkAsUsedAsync(
            resetToken.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidReset_InvalidatesEveryOtherLiveLink()
    {
        // Arrange — invalidate on use, not on request
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.InvalidateAllForUserAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AutomaticallyLockedAccount_ClearsTheLock()
    {
        // Arrange — locked by strangers' wrong passwords (timed, counter at the
        // threshold); a completed reset proves the mailbox, so nothing is left to guard
        var user = CreateLockedUser(failedLoginAttempts: 5, lockoutEnd: DateTime.UtcNow.AddMinutes(15));
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userRepositoryMock.Verify(r => r.UnlockAsync(
            user.Id, user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AdministrativelyLockedAccount_LeavesTheLockInPlace()
    {
        // Arrange — an administrator's lock: no expiry, counter untouched. A
        // self-service reset must never undo an incident-response decision.
        var user = CreateLockedUser(failedLoginAttempts: 0, lockoutEnd: null);
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userRepositoryMock.Verify(r => r.UnlockAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TimedAdministrativeLock_LeavesTheLockInPlace()
    {
        // Arrange — an administrator's 24-hour lock; Lock() zeroed the counter, so
        // the expiry alone must not make a self-service reset clear it
        var user = CreateLockedUser(failedLoginAttempts: 0, lockoutEnd: DateTime.UtcNow.AddHours(24));
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userRepositoryMock.Verify(r => r.UnlockAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static User CreateLockedUser(int failedLoginAttempts, DateTime? lockoutEnd) => new(
        id: Guid.NewGuid(),
        email: "john@example.com",
        normalizedEmail: "JOHN@EXAMPLE.COM",
        passwordHash: "OldHashedPassword",
        firstName: "John",
        lastName: "Doe",
        displayName: null,
        phoneNumber: null,
        status: UserStatus.Locked,
        emailConfirmed: true,
        phoneConfirmed: false,
        twoFactorEnabled: false,
        twoFactorSecret: null,
        failedLoginAttempts: failedLoginAttempts,
        lockoutEnd: lockoutEnd,
        lastLoginAt: null,
        passwordChangedAt: DateTime.UtcNow.AddDays(-30),
        mustChangePassword: false,
        preferredLanguage: "en",
        timeZone: "UTC",
        metadata: null,
        isSystemUser: false,
        createdAt: DateTime.UtcNow.AddDays(-60),
        createdBy: Guid.NewGuid(),
        modifiedAt: null,
        modifiedBy: null);

    [Fact]
    public async Task Handle_ActiveAccount_LeavesStatusAlone()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — Unlock also sets Active, so it must not run for an account that is not Locked
        _userRepositoryMock.Verify(r => r.UnlockAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TerminateSessionsTrue_RevokesEveryCredential()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: true);

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — the full wipe, not a session-row termination. Ending the rows
        // left the refresh tokens alive, and nothing on the refresh path consults
        // the session, so the reset used to lock nobody out.
        _credentialRevocationMock.Verify(s => s.RevokeAllCredentialsAsync(
            user.Id, user.Id, "Password reset", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminateSessionsFalse_StillRevokesTheSsoSessions()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: false);

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — preserving application sessions is a choice the operator may
        // make; preserving single sign-on is not. An SSO cookie that predates the
        // reset still mints authorization codes for every entitled application, so
        // it goes regardless of the flag and no caller can opt out.
        _credentialRevocationMock.Verify(s => s.RevokeAllCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _credentialRevocationMock.Verify(s => s.RevokeIdpSessionsAsync(
            user.Id, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidReset_SavesOldPasswordToHistory()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Captured before the act: the handler now mutates the aggregate, so reading
        // user.PasswordHash at assert time would return the NEW hash.
        var previousHash = user.PasswordHash;

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordHistoryRepositoryMock.Verify(r => r.AddAsync(
            It.Is<PasswordHistory>(ph => ph.UserId == user.Id && ph.PasswordHash == previousHash),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExternalOnlyUserWithoutPassword_DispatchesPasswordCreatedNotChanged()
    {
        // THE CENTRAL REGRESSION GUARD. Before this change the handler dispatched nothing at
        // all, so the moment an external-only super-admin acquired its first credential left
        // no audit row. It must be recorded, and recorded as a creation, not a rotation.
        var user = TestHelpers.CreateUser(email: "external@example.com", passwordHash: null);
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        await _handler.Handle(command, CancellationToken.None);

        _dispatchedEvents.Should().ContainSingle();
        var created = _dispatchedEvents[0].Should().BeOfType<PasswordCreatedEvent>().Subject;
        created.UserId.Should().Be(user.Id);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserWithAnExistingPassword_DispatchesPasswordChanged()
    {
        // The other side of the same branch: a rotation stays a rotation.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        await _handler.Handle(command, CancellationToken.None);

        _dispatchedEvents.Should().ContainSingle();
        _dispatchedEvents[0].Should().BeOfType<PasswordChangedEvent>();
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RejectedReset_DispatchesNothing()
    {
        // A reset that never happened must not announce that one did.
        var command = new ResetPasswordCommand("wrong-token", "NewPass1!");

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordResetToken?)null);

        await _handler.Handle(command, CancellationToken.None);

        _dispatchedEvents.Should().BeEmpty();
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupValidResetScenario(User user, PasswordResetToken resetToken, ResetPasswordCommand command)
    {
        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync($"hmac({command.Token})", It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(resetToken.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.NewPassword, It.IsAny<string>()))
            .Returns(false);

        _passwordHasherMock
            .Setup(h => h.HashPassword(command.NewPassword))
            .Returns("NewHashedPassword");

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(user.Id, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }
}
