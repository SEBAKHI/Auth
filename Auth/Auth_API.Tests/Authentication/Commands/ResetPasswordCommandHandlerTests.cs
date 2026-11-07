using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ResetPassword;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;
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
    // S20 (PK12): the passkeys the 72-hour revocation sees, on a fixed clock.
    private static readonly DateTimeOffset PasskeyNow = new(2026, 11, 5, 9, 14, 0, TimeSpan.Zero);
    private readonly Mock<Auth.Domain.Interfaces.Repositories.IUserPasskeyRepository> _passkeysMock = PasskeyTestDoubles.EmptyRepository();
    private readonly Mock<MediatR.IPublisher> _passkeyPublisherMock = new();

    private const string ValidToken = "valid-token";
    private const string ValidTokenHash = $"hmac({ValidToken})";

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITwoFactorStateStore> _twoFactorStateStoreMock = new();
    private readonly Mock<ITwoFactorChallengeRepository> _twoFactorChallengeRepositoryMock = new();
    private readonly Mock<IPasswordResetTokenRepository> _passwordResetTokenRepositoryMock;
    private readonly Mock<IPasswordHistoryRepository> _passwordHistoryRepositoryMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly FakeCredentialHasher _tokenKeyServiceMock;
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
        _tokenKeyServiceMock = new FakeCredentialHasher();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<ResetPasswordCommandHandler>>();

        _tokenKeyServiceMock.MapAll(token => $"hmac({token})");

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
            _tokenKeyServiceMock,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
            _eventDispatcherMock.Object,
            TestHelpers.CreateAccountLockout(_userRepositoryMock.Object, _passwordSettings),
            _twoFactorStateStoreMock.Object,
            _twoFactorChallengeRepositoryMock.Object,
            PasskeyTestDoubles.Revocation(_passkeysMock, _passkeyPublisherMock.Object, new FixedTimeProvider(PasskeyNow)),
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
    public async Task Handle_ValidReset_ClearsTheEmailedCodesCountAndLock()
    {
        // S24, LK4 (c): the reset proved the mailbox the emailed second factor goes to,
        // so it is that factor's way out of its lock — the authenticator app's count is
        // not a reset's to clear.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _twoFactorStateStoreMock.Verify(s => s.ClearEmailCodeLockAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _twoFactorStateStoreMock.Verify(s => s.TryResetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidReset_EndsEveryOpenChallenge_ThenRestoresTheEmailedCodesSendBudget()
    {
        // S24 PR B, OI-220 (3) and (5): an enrollment or a switch-off begun before the
        // reset is not finished after it, and the holder of the mailbox gets the send
        // caps back. Ended first, so a restored budget never reopens a live challenge.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        var order = new List<string>();
        _twoFactorChallengeRepositoryMock
            .Setup(r => r.InvalidateEveryPurposeForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("ended"))
            .Returns(Task.CompletedTask);
        _twoFactorChallengeRepositoryMock
            .Setup(r => r.RestoreEmailCodeSendBudgetAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("restored"))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal(["ended", "restored"]);
        _twoFactorChallengeRepositoryMock.Verify(
            r => r.InvalidateAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the sign-in-only invalidation is a new sign-in's, never the reset's");
    }

    [Fact]
    public async Task Handle_ValidReset_WhenTheChallengesCannotBeEnded_StillSucceeds()
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        _twoFactorChallengeRepositoryMock
            .Setup(r => r.InvalidateEveryPurposeForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Invalid column name 'Purpose'"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse("the password is already reset; the caps stay as they were, the safe side");
    }

    [Fact]
    public async Task Handle_ValidReset_ItsPostCommitSteps_AreNotStoppedByTheCaller()
    {
        // Review of #107 (F5): the password is changed once the link is spent, so the
        // client going away must not leave the challenges open or the lock and caps set.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        using var request = new CancellationTokenSource();

        await _handler.Handle(command, request.Token);

        _twoFactorChallengeRepositoryMock.Verify(r => r.InvalidateEveryPurposeForUserAsync(user.Id, CancellationToken.None), Times.Once);
        _twoFactorStateStoreMock.Verify(s => s.ClearEmailCodeLockAsync(user.Id, CancellationToken.None), Times.Once);
        _twoFactorChallengeRepositoryMock.Verify(r => r.RestoreEmailCodeSendBudgetAsync(user.Id, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidReset_WhenTheEmailedLockCannotBeCleared_StillEndsEveryOpenChallenge_First()
    {
        // Review of #107: a failure of the lock or the caps must not leave an enrollment
        // or a switch-off begun before the reset open after it.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        var order = new List<string>();
        _twoFactorChallengeRepositoryMock
            .Setup(r => r.InvalidateEveryPurposeForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("ended"))
            .Returns(Task.CompletedTask);
        _twoFactorStateStoreMock
            .Setup(s => s.ClearEmailCodeLockAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("emailed lock"))
            .ThrowsAsync(new InvalidOperationException("Invalid column name 'EmailFailedAttempts'."));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        order.Should().Equal(["ended", "emailed lock"]);
    }

    [Fact]
    public async Task Handle_TheEmailedCodesLockCannotBeCleared_TheLinkIsStillSpent_AndTheSessionsStillRevoked()
    {
        // Review 99, F4: the statement fails deterministically while the API runs ahead
        // of the schema (a missing column). It runs last and its failure is logged, so
        // the reset still evicts every session and spends the link. Since REVIEW-FIXES 150 (F14)
        // the link is spent first, before the first write, and the sessions end right after it.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: true);
        SetupValidResetScenario(user, resetToken, command);
        var order = new List<string>();
        _passwordResetTokenRepositoryMock
            .Setup(r => r.MarkAsUsedAsync(resetToken.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("link spent"))
            .ReturnsAsync(true);
        _credentialRevocationMock
            .Setup(r => r.RevokeAllCredentialsAsync(user.Id, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("sessions revoked"))
            .ReturnsAsync(1);
        _twoFactorStateStoreMock
            .Setup(s => s.ClearEmailCodeLockAsync(user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("emailed lock"))
            .ThrowsAsync(new InvalidOperationException("Invalid column name 'EmailFailedAttempts'."));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse("the password is changed; only the emailed code's lock is left as it was");
        order.Should().Equal("link spent", "sessions revoked", "emailed lock");
        _eventDispatcherMock.Verify(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Once);
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
            FakeCredentialHasher.CandidatesOf(ValidTokenHash), It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
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
            .Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf(ValidTokenHash), It.IsAny<CancellationToken>()))
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

        // Assert — one statement, which lifts the automatic lock and zeroes the count
        _userRepositoryMock.Verify(r => r.ClearAutomaticLockAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
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

        // Assert — the handler asks the one clearing statement, unconditionally,
        // and that statement spares an administrator's lock (count 0): see
        // SuccessRepairSqlTests. No status write runs here.
        _userRepositoryMock.Verify(r => r.ClearAutomaticLockAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.SetAccountStatusAsync(
            It.IsAny<Guid>(), It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // Assert — the handler asks the one clearing statement, unconditionally,
        // and that statement spares an administrator's lock (count 0): see
        // SuccessRepairSqlTests. No status write runs here.
        _userRepositoryMock.Verify(r => r.ClearAutomaticLockAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.SetAccountStatusAsync(
            It.IsAny<Guid>(), It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // Assert — the clearing statement runs (it starts a new count) and no
        // status write does: on an active row it changes nothing but the count
        _userRepositoryMock.Verify(r => r.ClearAutomaticLockAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _userRepositoryMock.Verify(r => r.SetAccountStatusAsync(
            It.IsAny<Guid>(), It.IsAny<AccountStatusChange>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordResetToken?)null);

        await _handler.Handle(command, CancellationToken.None);

        _dispatchedEvents.Should().BeEmpty();
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void SetupValidResetScenario(User user, PasswordResetToken resetToken, ResetPasswordCommand command)
    {
        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf($"hmac({command.Token})"), It.IsAny<CancellationToken>()))
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

        // The link is still unspent: this submission spends it (REVIEW-FIXES 150 F14).
        _passwordResetTokenRepositoryMock
            .Setup(r => r.MarkAsUsedAsync(resetToken.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// S20 (PK12): a password reset by email revokes the passkeys registered in the 72 hours
    /// before it — one an attacker added with the stolen password goes — and the notice lists
    /// those that stay with their dates.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the revocation dropped from this path.</remarks>
    [Fact]
    public async Task Handle_ValidReset_RevokesThePasskeysOfTheLast72Hours_AndListsTheOthers()
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        var cutoff = PasskeyNow.UtcDateTime.AddHours(-72);
        var recent = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        var older = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-73), "Older");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent, older);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _passkeysMock.Verify(r => r.RevokeRegisteredAfterAsync(
            user.Id, cutoff, Auth.Domain.Constants.PasskeyRemovalReasons.PasswordReset, user.Id, CancellationToken.None), Times.Once,
            "a passkey registered in the 72 hours before is revoked; the statement keeps the older ones (CreatedAt > @RegisteredAfter)");
        _passkeyPublisherMock.Verify(p => p.Publish(
            It.Is<Auth.Domain.Events.PasskeyRemovedEvent>(e => e.PasskeyId == recent.Id
                && e.Reason == Auth.Domain.Constants.PasskeyRemovalReasons.PasswordReset && e.RemovedBy == user.Id),
            CancellationToken.None), Times.Once);
        _dispatchedEvents.OfType<Auth.Domain.Events.PasswordChangedEvent>().Single().Passkeys
            .Should().Equal(new Auth.Domain.Events.PasskeySummary("Older", older.CreatedAt, older.LastUsedAt));
    }

    /// <summary>
    /// REVIEW 150 F12 (D-150-2 (a)): a reset that gives a passwordless account its first
    /// password revokes the passkeys of the 72 hours before it too, and the password-created
    /// notice lists the ones that stay, as the password-changed notice does.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the first password's event raised without the list.</remarks>
    [Fact]
    public async Task Handle_AFirstPasswordSetByReset_RevokesTheRecentPasskeys_AndTheNoticeListsTheOthers()
    {
        var user = TestHelpers.CreateUser(email: "external@example.com", passwordHash: null);
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        var recent = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        var older = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-73), "Older");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent, older);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _passkeyPublisherMock.Verify(p => p.Publish(
            It.Is<Auth.Domain.Events.PasskeyRemovedEvent>(e => e.PasskeyId == recent.Id
                && e.Reason == Auth.Domain.Constants.PasskeyRemovalReasons.PasswordReset),
            CancellationToken.None), Times.Once, "its owner is told which passkey went, and why");
        _dispatchedEvents.OfType<PasswordCreatedEvent>().Single().Passkeys
            .Should().Equal(new Auth.Domain.Events.PasskeySummary("Older", older.CreatedAt, older.LastUsedAt));
    }

    /// <summary>
    /// REVIEW 150 F2: a revocation that fails never fails the reset, is logged once at Error,
    /// and the notice's list, read after it, shows the recent passkey too.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the list read before the revocation again.</remarks>
    [Fact]
    public async Task Handle_ValidReset_WhenTheRevocationFails_TheNoticeListsTheRecentPasskeyToo()
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");
        SetupValidResetScenario(user, resetToken, command);
        var recent = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        var older = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-73), "Older");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: true, recent, older);
        var log = new CapturingLogger<Auth.Application.Features.Passkeys.Common.RecentPasskeyRevocation>();

        var result = await Handler(log).Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse("the password is already reset");
        log.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Message.Should().StartWith(Auth.Domain.Constants.PasskeySignalCodes.RevocationFailed);
        _dispatchedEvents.OfType<PasswordChangedEvent>().Single().Passkeys.Select(passkey => passkey.Name)
            .Should().BeEquivalentTo(["Recent", "Older"], "a passkey the revocation left live still signs in, so the notice lists it");
    }

    /// <summary>
    /// REVIEW-FIXES 150 F14 (OI-31): of two submissions of one link, only the one that spends it
    /// goes on. The spend is conditional and its row count read, before the first write, so the
    /// other is refused as an invalid link and writes nothing.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the spend's answer ignored.</remarks>
    [Fact]
    public async Task Handle_TwoSubmissionsOfOneLink_TheSecondLosesTheSpend_AndWritesNothing()
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: true);
        SetupValidResetScenario(user, resetToken, command);
        // Both found the link unspent; only the first spends it.
        _passwordResetTokenRepositoryMock
            .SetupSequence(r => r.MarkAsUsedAsync(resetToken.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var first = await _handler.Handle(command, CancellationToken.None);
        var second = await _handler.Handle(command, CancellationToken.None);

        first.IsError.Should().BeFalse();
        second.IsError.Should().BeTrue();
        second.FirstError.Code.Should().Be(PasswordResetErrors.InvalidOrExpiredToken.Code, "the first submission spent the link");
        _userRepositoryMock.Verify(r => r.UpdatePasswordAsync(
            user.Id, It.IsAny<string>(), user.Id, It.IsAny<CancellationToken>()), Times.Once, "the password is written once");
        _passwordHistoryRepositoryMock.Verify(r => r.AddAsync(It.IsAny<PasswordHistory>(), It.IsAny<CancellationToken>()), Times.Once);
        _credentialRevocationMock.Verify(s => s.RevokeAllCredentialsAsync(
            user.Id, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventDispatcherMock.Verify(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// REVIEW 150 F1, F2 and REVIEW-FIXES 150 F14: once the link is spent, a client that
    /// disconnects cannot skip any step. The link goes before the password is written, and
    /// every step after it — the sessions, the passkeys and their list, the lock, the other
    /// links, the history, the event — runs with no request token.
    /// </summary>
    /// <remarks>
    /// Deliberate breaks, each verified red: the request's token back on the dispatch; the
    /// spend moved after the write again.
    /// </remarks>
    [Fact]
    public async Task Handle_ACallerThatDisconnectsAfterTheWrite_CannotSkipAnyStep()
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: true);
        SetupValidResetScenario(user, resetToken, command);
        using var request = new CancellationTokenSource();
        var order = new List<string>();
        _passwordResetTokenRepositoryMock
            .Setup(r => r.MarkAsUsedAsync(resetToken.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                order.Add("link spent");
                return Task.FromResult(true);
            });
        _userRepositoryMock
            .Setup(r => r.UpdatePasswordAsync(user.Id, "NewHashedPassword", user.Id, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                order.Add("password written");
                request.Cancel();
            })
            .Returns(Task.CompletedTask);
        // Every later step that takes a token stops on a cancelled one, as a database call does.
        _credentialRevocationMock
            .Setup(s => s.RevokeAllCredentialsAsync(user.Id, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid? _, string _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(1);
            });
        _passwordResetTokenRepositoryMock
            .Setup(r => r.InvalidateAllForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .Returns((Guid _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        _passwordHistoryRepositoryMock
            .Setup(r => r.CleanupOldHistoryAsync(user.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(It.IsAny<AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Returns((AggregateRoot aggregate, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                _dispatchedEvents.AddRange(aggregate.DomainEvents);
                aggregate.ClearDomainEvents();
                return Task.CompletedTask;
            });
        var recent = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent);

        var result = await _handler.Handle(command, request.Token);

        result.IsError.Should().BeFalse("the link is spent and the password written: the rest runs whatever the client does");
        order.Should().Equal(["link spent", "password written"], "the link is spent before the first write");
        _credentialRevocationMock.Verify(s => s.RevokeAllCredentialsAsync(
            user.Id, user.Id, "Password reset", CancellationToken.None), Times.Exactly(2),
            "the sessions end whatever the client does, and once more after the recent passkey went (REVIEW 151 F2)");
        _passkeysMock.Verify(r => r.RevokeRegisteredAfterAsync(
            user.Id, It.IsAny<DateTime>(), Auth.Domain.Constants.PasskeyRemovalReasons.PasswordReset, user.Id, CancellationToken.None), Times.Once,
            "the recent passkeys go whatever the client does");
        _passkeysMock.Verify(r => r.GetLiveAsync(user.Id, PasskeyTestDoubles.RpId, CancellationToken.None), Times.Once,
            "the notice's list is read whatever the client does");
        _userRepositoryMock.Verify(r => r.ClearAutomaticLockAsync(user.Id, CancellationToken.None), Times.Once, "the lock is lifted");
        _passwordResetTokenRepositoryMock.Verify(r => r.InvalidateAllForUserAsync(user.Id, CancellationToken.None), Times.Once,
            "the account's other links die");
        _passwordHistoryRepositoryMock.Verify(r => r.CleanupOldHistoryAsync(user.Id, _passwordSettings.HistoryCount, CancellationToken.None), Times.Once);
        _eventDispatcherMock.Verify(d => d.DispatchEventsAsync(user, CancellationToken.None), Times.Once, "the owner is told");
        _dispatchedEvents.OfType<PasswordChangedEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// REVIEW-FIXES 150 F14: the recent passkeys go even when ending the sessions throws, and
    /// that exception then surfaces as before.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the revocation moved out of the <c>finally</c>.</remarks>
    [Fact]
    public async Task Handle_WhenEndingTheSessionsThrows_TheRecentPasskeysStillGo_AndTheErrorSurfaces()
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: true);
        SetupValidResetScenario(user, resetToken, command);
        _credentialRevocationMock
            .Setup(s => s.RevokeAllCredentialsAsync(user.Id, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the token store is unreachable"));
        var recent = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the token store is unreachable");
        _passkeysMock.Verify(r => r.RevokeRegisteredAfterAsync(
            user.Id, It.IsAny<DateTime>(), Auth.Domain.Constants.PasskeyRemovalReasons.PasswordReset, user.Id, CancellationToken.None), Times.Once,
            "the passkeys of the last 72 hours go whatever ending the sessions did");
    }

    private ResetPasswordCommandHandler Handler(Microsoft.Extensions.Logging.ILogger<Auth.Application.Features.Passkeys.Common.RecentPasskeyRevocation> log) =>
        new(
            _userRepositoryMock.Object,
            _passwordResetTokenRepositoryMock.Object,
            _passwordHistoryRepositoryMock.Object,
            _credentialRevocationMock.Object,
            _passwordHasherMock.Object,
            _tokenKeyServiceMock,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
            _eventDispatcherMock.Object,
            TestHelpers.CreateAccountLockout(_userRepositoryMock.Object, _passwordSettings),
            _twoFactorStateStoreMock.Object,
            _twoFactorChallengeRepositoryMock.Object,
            PasskeyTestDoubles.Revocation(_passkeysMock, _passkeyPublisherMock.Object, new FixedTimeProvider(PasskeyNow), log),
            _loggerMock.Object);

    /// <summary>
    /// REVIEW 151 F2: a reset that revoked a recent passkey sweeps once more after it: every
    /// credential, or, with the application sessions kept by configuration, the single sign-on
    /// sessions, which a reset always ends.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the second sweep dropped.</remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_AResetThatRevokesARecentPasskey_SweepsOnceMoreAfterIt(bool terminateSessions)
    {
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: terminateSessions);
        SetupValidResetScenario(user, resetToken, command);
        var calls = new List<string>();
        _credentialRevocationMock
            .Setup(s => s.RevokeAllCredentialsAsync(user.Id, It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("sessions"))
            .ReturnsAsync(1);
        _credentialRevocationMock
            .Setup(s => s.RevokeIdpSessionsAsync(user.Id, null, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("sso"))
            .ReturnsAsync(1);
        var recent = PasskeyTestDoubles.Live(user.Id, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        _passkeysMock
            .Setup(r => r.RevokeRegisteredAfterAsync(user.Id, It.IsAny<DateTime>(), Auth.Domain.Constants.PasskeyRemovalReasons.PasswordReset, user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("passkeys"))
            .ReturnsAsync([recent]);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeFalse();
        var sweep = terminateSessions ? "sessions" : "sso";
        calls.Should().Equal([sweep, "passkeys", sweep]);
    }
}
