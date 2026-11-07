using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ChangePassword;
using Auth.Application.Interfaces;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for ChangePasswordCommandHandler.
/// </summary>
public class ChangePasswordCommandHandlerTests
{
    // S20 (PK12): the passkeys the 72-hour revocation sees, on a fixed clock.
    private static readonly DateTimeOffset PasskeyNow = new(2026, 11, 5, 9, 14, 0, TimeSpan.Zero);
    private readonly Mock<Auth.Domain.Interfaces.Repositories.IUserPasskeyRepository> _passkeysMock = PasskeyTestDoubles.EmptyRepository();
    private readonly Mock<MediatR.IPublisher> _passkeyPublisherMock = new();

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordHistoryRepository> _passwordHistoryRepositoryMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<ChangePasswordCommandHandler>> _loggerMock;
    private readonly PasswordSettings _passwordSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly PasswordValidator _passwordValidator;
    private readonly ChangePasswordCommandHandler _handler;

    public ChangePasswordCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordHistoryRepositoryMock = new Mock<IPasswordHistoryRepository>();
        _credentialRevocationMock = new Mock<ICredentialRevocationService>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<ChangePasswordCommandHandler>>();

        _passwordSettings = TestHelpers.CreatePasswordSettings();
        _sessionSettings = TestHelpers.CreateSessionSettings();

        _passwordValidator = new PasswordValidator(
            TestHelpers.CreateOptions(_passwordSettings));

        _handler = new ChangePasswordCommandHandler(
            _userRepositoryMock.Object,
            _passwordHistoryRepositoryMock.Object,
            _credentialRevocationMock.Object,
            _passwordHasherMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            _eventDispatcherMock.Object,
            TestHelpers.CreateAccountLockout(_userRepositoryMock.Object, _passwordSettings),
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
            PasskeyTestDoubles.Revocation(_passkeysMock, _passkeyPublisherMock.Object, new FixedTimeProvider(PasskeyNow)),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidChange_UpdatesPasswordAndReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", It.IsAny<string>()))
            .Returns(false);

        _passwordHasherMock
            .Setup(h => h.HashPassword("NewPass1!"))
            .Returns("NewHashedPassword");

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdatePasswordAsync(
            userId, "NewHashedPassword", userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Handle_InvalidCurrentPassword_ReturnsInvalidCurrentPasswordError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "WrongPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("WrongPass1!", user.PasswordHash!))
            .Returns(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidCurrentPassword");
    }

    [Fact]
    public async Task Handle_ExternalOnlyUserWithoutPassword_ReturnsInvalidCurrentPasswordError()
    {
        // Arrange - an external-only account has no password to change; the
        // guard must answer cleanly instead of feeding null to the hasher.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, passwordHash: null);
        var command = new ChangePasswordCommand(userId, "Irrelevant1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidCurrentPassword");
        _passwordHasherMock.Verify(
            h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WeakNewPassword_ReturnsValidationErrors()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "weak");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!))
            .Returns(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code.StartsWith("Password."));
    }

    [Fact]
    public async Task Handle_ReusedPasswordFromHistory_ReturnsPasswordRecentlyUsedError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!))
            .Returns(true);

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "OldHistoricalHash" });

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", "OldHistoricalHash"))
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
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!))
            .Returns(true);

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // NewPass1! matches current password hash
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
    public async Task Handle_ValidChange_SavesOldPasswordToHistory()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var originalPasswordHash = user.PasswordHash;
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", originalPasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", It.IsAny<string>()))
            .Returns(false);

        _passwordHasherMock
            .Setup(h => h.HashPassword("NewPass1!"))
            .Returns("NewHashedPassword");

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert - use captured original hash since user.PasswordHash gets mutated by ChangePassword
        _passwordHistoryRepositoryMock.Verify(r => r.AddAsync(
            It.Is<PasswordHistory>(ph => ph.UserId == userId && ph.PasswordHash == originalPasswordHash),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidChange_CleansUpOldHistory()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", It.IsAny<string>()))
            .Returns(false);

        _passwordHasherMock
            .Setup(h => h.HashPassword("NewPass1!"))
            .Returns("NewHashedPassword");

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordHistoryRepositoryMock.Verify(r => r.CleanupOldHistoryAsync(
            userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminateSessionsTrue_WithCurrentSessionId_RevokesOtherCredentialsAndSparesThisBrowser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(
            userId, "OldPass1!", "NewPass1!",
            TerminateSessions: true,
            CurrentSessionId: currentSessionId,
            IdpSessionToken: "sso-cookie");

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — through the revocation service, which is what also revokes the
        // refresh tokens, blacklists the session ids and revokes the SSO sessions.
        // Terminating session rows alone left every one of those alive.
        _credentialRevocationMock.Verify(s => s.RevokeCredentialsAsync(
            userId, currentSessionId, "sso-cookie", userId, "Password changed",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminateSessionsTrue_WithoutCurrentSessionId_RevokesEverything()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: true);

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _credentialRevocationMock.Verify(s => s.RevokeCredentialsAsync(
            userId, null, null, userId, "Password changed",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminateSessionsFalse_RevokesNothing()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: false);

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _credentialRevocationMock.Verify(s => s.RevokeCredentialsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<Guid?>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidChange_DispatchesDomainEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!");

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _eventDispatcherMock.Verify(d => d.DispatchEventsAsync(
            user, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupSuccessfulPasswordChange(Guid userId, User user)
    {
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!))
            .Returns(true);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", It.IsAny<string>()))
            .Returns(false);

        _passwordHasherMock
            .Setup(h => h.HashPassword("NewPass1!"))
            .Returns("NewHashedPassword");

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    /// <summary>
    /// S20 (PK12, ARR-0667): a password change revokes the passkeys registered in the 72 hours
    /// before it, with its reason, and the notice lists those that stay with their dates.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the revocation dropped from this path.</remarks>
    [Fact]
    public async Task Handle_ValidChange_RevokesThePasskeysOfTheLast72Hours_AndListsTheOthers()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash!)).Returns(true);
        _passwordHasherMock.Setup(h => h.VerifyPassword("NewPass1!", It.IsAny<string>())).Returns(false);
        _passwordHasherMock.Setup(h => h.HashPassword("NewPass1!")).Returns("NewHashedPassword");
        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        var cutoff = PasskeyNow.UtcDateTime.AddHours(-72);
        var recent = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        var older = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-73), "Older");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent, older);

        var result = await _handler.Handle(new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _passkeysMock.Verify(r => r.RevokeRegisteredAfterAsync(
            userId, cutoff, Auth.Domain.Constants.PasskeyRemovalReasons.PasswordChange, userId, CancellationToken.None), Times.Once,
            "a passkey registered in the 72 hours before is revoked; the statement keeps the older ones (CreatedAt > @RegisteredAfter)");
        _passkeyPublisherMock.Verify(p => p.Publish(
            It.Is<Auth.Domain.Events.PasskeyRemovedEvent>(e => e.PasskeyId == recent.Id
                && e.Reason == Auth.Domain.Constants.PasskeyRemovalReasons.PasswordChange && e.RemovedBy == userId
                && e.Name == "Recent" && e.AddedAtUtc == recent.CreatedAt),
            CancellationToken.None), Times.Once);
        user.DomainEvents.OfType<Auth.Domain.Events.PasswordChangedEvent>().Single().Passkeys
            .Should().Equal(new Auth.Domain.Events.PasskeySummary("Older", older.CreatedAt, older.LastUsedAt));
    }

    /// <summary>
    /// REVIEW 150 F2: a revocation that fails never fails the change, is logged once at Error,
    /// and the notice's list, read after it, shows the recent passkey too: the one the owner
    /// most needs to see is never the one it hides.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the list read before the revocation again.</remarks>
    [Fact]
    public async Task Handle_ValidChange_WhenTheRevocationFails_TheNoticeListsTheRecentPasskeyToo()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        SetupSuccessfulPasswordChange(userId, user);
        var recent = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        var older = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-73), "Older");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: true, recent, older);
        var log = new CapturingLogger<Auth.Application.Features.Passkeys.Common.RecentPasskeyRevocation>();

        var result = await Handler(log).Handle(new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!"), CancellationToken.None);

        result.IsError.Should().BeFalse("the password is already changed");
        log.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Message.Should().StartWith(Auth.Domain.Constants.PasskeySignalCodes.RevocationFailed);
        user.DomainEvents.OfType<Auth.Domain.Events.PasswordChangedEvent>().Single().Passkeys.Select(passkey => passkey.Name)
            .Should().BeEquivalentTo(["Recent", "Older"], "a passkey the revocation left live still signs in, so the notice lists it");
    }

    /// <summary>
    /// REVIEW 150 F1, F2 and REVIEW-FIXES 150 F14: from the write on, a client that disconnects
    /// cannot skip any step. The sessions, the passkeys and their list, the lock, the history and
    /// the event all run with no request token, so the owner is still told.
    /// </summary>
    /// <remarks>
    /// Deliberate breaks, each verified red: the request's token back on the dispatch; the
    /// revocation moved back to the end.
    /// </remarks>
    [Fact]
    public async Task Handle_ACallerThatDisconnectsAfterTheWrite_CannotSkipAnyStep()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        SetupSuccessfulPasswordChange(userId, user);
        using var request = new CancellationTokenSource();
        _userRepositoryMock
            .Setup(r => r.UpdatePasswordAsync(userId, "NewHashedPassword", userId, It.IsAny<CancellationToken>()))
            .Callback(() => request.Cancel())
            .Returns(Task.CompletedTask);
        // Every later step that takes a token stops on a cancelled one, as a database call does.
        _credentialRevocationMock
            .Setup(s => s.RevokeCredentialsAsync(userId, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Guid? _, string? _, Guid? _, string _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(1);
            });
        _passwordHistoryRepositoryMock
            .Setup(r => r.CleanupOldHistoryAsync(userId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, int _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        _eventDispatcherMock
            .Setup(d => d.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
            .Returns((Auth.Domain.Primitives.AggregateRoot _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var recent = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent);

        var result = await _handler.Handle(
            new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: true), request.Token);

        result.IsError.Should().BeFalse("the password is written: the rest runs whatever the client does");
        _credentialRevocationMock.Verify(s => s.RevokeCredentialsAsync(
            userId, It.IsAny<Guid?>(), It.IsAny<string?>(), userId, "Password changed", CancellationToken.None), Times.Exactly(2),
            "the sessions end whatever the client does, and once more after the recent passkey went (REVIEW 151 F2)");
        _passkeysMock.Verify(r => r.RevokeRegisteredAfterAsync(
            userId, It.IsAny<DateTime>(), Auth.Domain.Constants.PasskeyRemovalReasons.PasswordChange, userId, CancellationToken.None), Times.Once,
            "the recent passkeys go whatever the client does");
        _passkeysMock.Verify(r => r.GetLiveAsync(userId, PasskeyTestDoubles.RpId, CancellationToken.None), Times.Once);
        _userRepositoryMock.Verify(r => r.ClearAutomaticLockAsync(userId, CancellationToken.None), Times.Once);
        _passwordHistoryRepositoryMock.Verify(r => r.CleanupOldHistoryAsync(userId, It.IsAny<int>(), CancellationToken.None), Times.Once);
        _eventDispatcherMock.Verify(d => d.DispatchEventsAsync(user, CancellationToken.None), Times.Once, "the owner is still told");
    }

    /// <summary>
    /// REVIEW-FIXES 150 F14: the recent passkeys go even when ending the other sessions throws,
    /// and that exception then surfaces as before.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the revocation moved out of the <c>finally</c>.</remarks>
    [Fact]
    public async Task Handle_WhenEndingTheSessionsThrows_TheRecentPasskeysStillGo_AndTheErrorSurfaces()
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        SetupSuccessfulPasswordChange(userId, user);
        _credentialRevocationMock
            .Setup(s => s.RevokeCredentialsAsync(userId, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the token store is unreachable"));
        var recent = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        PasskeyTestDoubles.AsTable(_passkeysMock, revocationFails: false, recent);

        var act = () => _handler.Handle(
            new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: true), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the token store is unreachable");
        _passkeysMock.Verify(r => r.RevokeRegisteredAfterAsync(
            userId, It.IsAny<DateTime>(), Auth.Domain.Constants.PasskeyRemovalReasons.PasswordChange, userId, CancellationToken.None), Times.Once,
            "the passkeys of the last 72 hours go whatever ending the sessions did");
    }

    private ChangePasswordCommandHandler Handler(Microsoft.Extensions.Logging.ILogger<Auth.Application.Features.Passkeys.Common.RecentPasskeyRevocation> log) =>
        new(
            _userRepositoryMock.Object,
            _passwordHistoryRepositoryMock.Object,
            _credentialRevocationMock.Object,
            _passwordHasherMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            _eventDispatcherMock.Object,
            TestHelpers.CreateAccountLockout(_userRepositoryMock.Object, _passwordSettings),
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
            PasskeyTestDoubles.Revocation(_passkeysMock, _passkeyPublisherMock.Object, new FixedTimeProvider(PasskeyNow), log),
            _loggerMock.Object);

    /// <summary>
    /// REVIEW 151 F2: a change that revoked a recent passkey ends the other sessions once more after
    /// it, so a passkey sign-in in flight whose session was written after the first sweep is ended.
    /// With the sessions kept by configuration nothing is swept, before or after.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the second sweep dropped.</remarks>
    [Theory]
    [InlineData(true, new[] { "sessions", "passkeys", "sessions" })]
    [InlineData(false, new[] { "passkeys" })]
    public async Task Handle_AChangeThatRevokesARecentPasskey_EndsTheOtherSessionsOnceMoreAfterIt(bool terminateSessions, string[] expected)
    {
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        SetupSuccessfulPasswordChange(userId, user);
        var calls = new List<string>();
        _credentialRevocationMock
            .Setup(s => s.RevokeCredentialsAsync(userId, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("sessions"))
            .ReturnsAsync(1);
        var recent = PasskeyTestDoubles.Live(userId, PasskeyNow.UtcDateTime.AddHours(-71), "Recent");
        _passkeysMock
            .Setup(r => r.RevokeRegisteredAfterAsync(userId, It.IsAny<DateTime>(), Auth.Domain.Constants.PasskeyRemovalReasons.PasswordChange, userId, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("passkeys"))
            .ReturnsAsync([recent]);

        var result = await _handler.Handle(
            new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: terminateSessions), CancellationToken.None);

        result.IsError.Should().BeFalse();
        calls.Should().Equal(expected);
    }
}
