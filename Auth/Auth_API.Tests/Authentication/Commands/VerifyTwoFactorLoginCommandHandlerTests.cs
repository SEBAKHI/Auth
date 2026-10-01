using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for VerifyTwoFactorLoginCommandHandler. The real two-phase verifier
/// and its strategies run under the handler; only storage, TOTP arithmetic and
/// decryption are stubbed, so these tests follow a code from the reservation
/// through the check to the commit.
/// </summary>
public class VerifyTwoFactorLoginCommandHandlerTests
{
    private const string ChallengeToken = "challenge-token";
    private const string ChallengeTokenHash = "challenge-token-hash";
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "TESTSECRET";

    private readonly Mock<ITwoFactorChallengeRepository> _challengeRepositoryMock;
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<ITwoFactorSecretProtector> _secretProtectorMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepositoryMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<VerifyTwoFactorLoginCommandHandler>> _loggerMock;
    private readonly VerifyTwoFactorLoginCommandHandler _handler;

    public VerifyTwoFactorLoginCommandHandlerTests()
    {
        _challengeRepositoryMock = new Mock<ITwoFactorChallengeRepository>();
        _stateStoreMock = new Mock<ITwoFactorStateStore>();
        _totpServiceMock = new Mock<ITotpService>();
        _secretProtectorMock = new Mock<ITwoFactorSecretProtector>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loginAttemptRepositoryMock = new Mock<ILoginAttemptRepository>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _loginResponseBuilderMock = new Mock<ILoginResponseBuilder>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<VerifyTwoFactorLoginCommandHandler>>();

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(ChallengeToken))
            .Returns(ChallengeTokenHash);

        var verifier = new SecondFactorVerifier(
            _stateStoreMock.Object,
            [
                new TotpProofStrategy(_totpServiceMock.Object, _secretProtectorMock.Object),
                new RecoveryCodeProofStrategy(_totpServiceMock.Object)
            ]);

        _handler = new VerifyTwoFactorLoginCommandHandler(
            _challengeRepositoryMock.Object,
            verifier,
            _stateStoreMock.Object,
            _userRepositoryMock.Object,
            _loginAttemptRepositoryMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _loginResponseBuilderMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    private static VerifyTwoFactorLoginCommand CreateCommand(
        string code = "123456",
        bool useRecoveryCode = false)
        => new(ChallengeToken, code, useRecoveryCode, "127.0.0.1", "TestAgent/1.0");

    private static LoginResponse CreateLoginResponse() => new()
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

    private void SetupChallenge(TwoFactorChallenge? challenge)
    {
        _challengeRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(ChallengeTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
    }

    private void SetupFactor(Guid userId, string? recoveryCodes = null, DateTime? lockedUntil = null)
    {
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                userId, ProtectedSecret, recoveryCodes, isEnabled: true, failedAttempts: 0, lockedUntil));
        _stateStoreMock
            .Setup(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _secretProtectorMock
            .Setup(p => p.UnprotectAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlainSecret);
    }

    private void SetupHappyPath(
        Guid userId,
        out User user,
        out TwoFactorChallenge challenge,
        string? recoveryCodes = null,
        int attemptCount = 0)
    {
        // attemptCount lets a test start the challenge partway through its
        // allowance, which is the only way to reach the rejection that ends the
        // ceremony rather than merely counting against it.
        var created = new TwoFactorChallenge(
            Guid.NewGuid(),
            userId,
            ChallengeTokenHash,
            "127.0.0.1",
            expiresAt: DateTime.UtcNow.AddMinutes(TwoFactorChallenge.DefaultLifetimeMinutes),
            usedAt: null,
            attemptCount: attemptCount,
            createdAt: DateTime.UtcNow);
        SetupChallenge(created);
        challenge = created;

        // Both reservations and the commit answer whether THIS caller won. A loose
        // mock would answer null or zero and turn every happy path here into a
        // refusal, so the winning answers have to be stated.
        _challengeRepositoryMock
            .Setup(r => r.TryReserveAttemptAsync(created.Id, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attemptCount + 1);
        _stateStoreMock
            .Setup(s => s.TryCommitLoginAsync(
                created.Id, userId, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        user = TestHelpers.CreateUser(id: userId, twoFactorEnabled: true);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        SetupFactor(userId, recoveryCodes);
    }

    private void SetupBuild(User user, LoginResponse response)
    {
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                user, "127.0.0.1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(response);
    }

    private void VerifyNoAttemptReserved()
    {
        _stateStoreMock.Verify(
            s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _challengeRepositoryMock.Verify(
            r => r.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void VerifyNoCodeChecked()
    {
        _secretProtectorMock.Verify(
            p => p.UnprotectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _totpServiceMock.Verify(s => s.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _totpServiceMock.Verify(s => s.VerifyRecoveryCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private void VerifyNothingIssued()
    {
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    // ── The challenge gate (a read: the fast path, costs no attempt) ───────

    [Fact]
    public async Task Handle_UnknownChallenge_ReturnsChallengeInvalid()
    {
        // Arrange
        SetupChallenge(null);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        VerifyNoAttemptReserved();
    }

    [Fact]
    public async Task Handle_ExpiredChallenge_ReturnsChallengeInvalid()
    {
        // Arrange
        var challenge = new TwoFactorChallenge(
            Guid.NewGuid(), Guid.NewGuid(), ChallengeTokenHash, null,
            expiresAt: DateTime.UtcNow.AddMinutes(-1),
            usedAt: null, attemptCount: 0, createdAt: DateTime.UtcNow.AddMinutes(-6));
        SetupChallenge(challenge);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        VerifyNoAttemptReserved();
    }

    [Fact]
    public async Task Handle_UsedChallenge_ReturnsChallengeInvalid()
    {
        // Arrange
        var challenge = new TwoFactorChallenge(
            Guid.NewGuid(), Guid.NewGuid(), ChallengeTokenHash, null,
            expiresAt: DateTime.UtcNow.AddMinutes(4),
            usedAt: DateTime.UtcNow, attemptCount: 0, createdAt: DateTime.UtcNow);
        SetupChallenge(challenge);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        VerifyNoAttemptReserved();
    }

    [Fact]
    public async Task Handle_AttemptsExhaustedChallenge_ReturnsChallengeInvalid()
    {
        // Arrange
        var challenge = new TwoFactorChallenge(
            Guid.NewGuid(), Guid.NewGuid(), ChallengeTokenHash, null,
            expiresAt: DateTime.UtcNow.AddMinutes(4),
            usedAt: null, attemptCount: TwoFactorChallenge.MaxAttempts, createdAt: DateTime.UtcNow);
        SetupChallenge(challenge);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        VerifyNoAttemptReserved();
    }

    // ── The account reservation (A2) ───────────────────────────────────────

    [Fact]
    public async Task Handle_TwoFactorNotEnabled_ReturnsChallengeInvalid()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var challenge = TwoFactorChallenge.Create(userId, ChallengeTokenHash, null);
        SetupChallenge(challenge);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));

        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorSnapshot?)null);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        VerifyNoAttemptReserved();
    }

    [Fact]
    public async Task Handle_TwoFactorLocked_ReturnsLockedOutWithoutEvaluatingCode()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var challenge = TwoFactorChallenge.Create(userId, ChallengeTokenHash, null);
        SetupChallenge(challenge);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, twoFactorEnabled: true));

        SetupFactor(userId, lockedUntil: DateTime.UtcNow.AddMinutes(10));

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert: refused by the reservation phase, before any attempt is reserved
        // on the challenge and before any code is checked.
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.LockedOut");

        VerifyNoAttemptReserved();
        VerifyNoCodeChecked();
    }

    [Fact]
    public async Task Handle_InactiveAccount_ReturnsAccountInactiveError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var challenge = TwoFactorChallenge.Create(userId, ChallengeTokenHash, null);
        SetupChallenge(challenge);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, status: UserStatus.Inactive));

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.AccountInactive");
        VerifyNoAttemptReserved();
    }

    // ── The challenge reservation (A1) and the check ───────────────────────

    [Fact]
    public async Task Handle_ChallengeReserveDenied()
    {
        // The read saw a live challenge, but its allowance went to concurrent
        // requests before this one could reserve: nothing may be checked. The
        // account attempt already reserved stays counted.
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out var challenge);
        _challengeRepositoryMock
            .Setup(r => r.TryReserveAttemptAsync(challenge.Id, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _totpServiceMock.Setup(s => s.ValidateCode(PlainSecret, "123456")).Returns(true);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        VerifyNoCodeChecked();
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _stateStoreMock.Verify(
            s => s.TryCommitLoginAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNothingIssued();
    }

    [Fact]
    public async Task Handle_WrongTotpCode_RecordsFailureAndReturnsInvalidCode()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out var challenge);

        _totpServiceMock
            .Setup(s => s.ValidateCode(PlainSecret, "000000"))
            .Returns(false);

        // Act
        var result = await _handler.Handle(CreateCommand(code: "000000"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");

        // The failure was counted before the check, on the account and on the
        // challenge — and nothing writes the factor's row afterwards.
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _challengeRepositoryMock.Verify(
            r => r.TryReserveAttemptAsync(challenge.Id, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()),
            Times.Once);
        _stateStoreMock.Verify(
            s => s.TryCommitLoginAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // A rejected code does not end the ceremony, so it writes no row of its
        // own: the count rides on the challenge and the one row this sign-in owns
        // is settled only when the allowance runs out. One row per sign-in, not
        // one per guess.
        _loginAttemptRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _loginAttemptRepositoryMock.Verify(
            r => r.ResolveTwoFactorCeremonyAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNothingIssued();
    }

    [Theory]
    [InlineData(TwoFactorChallenge.MaxAttempts, true)]
    [InlineData(TwoFactorChallenge.MaxAttempts - 1, false)]
    public async Task Handle_FifthWrongCode_SettlesCeremony(int reservedAttempt, bool settles)
    {
        // The count comes from the reservation, not from the read: under a burst
        // the read is stale, and only the request whose reservation spent the last
        // attempt may close the ceremony.
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out var challenge);
        _challengeRepositoryMock
            .Setup(r => r.TryReserveAttemptAsync(challenge.Id, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservedAttempt);
        _totpServiceMock.Setup(s => s.ValidateCode(PlainSecret, "000000")).Returns(false);

        var result = await _handler.Handle(CreateCommand(code: "000000"), CancellationToken.None);

        result.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");
        _loginAttemptRepositoryMock.Verify(
            r => r.ResolveTwoFactorCeremonyAsync(
                challenge.Id, false, "Too many incorrect verification codes", It.IsAny<CancellationToken>()),
            settles ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Handle_LastAllowedWrongCode_SettlesTheCeremonyAsFailed()
    {
        // Arrange: the challenge has already burned its allowance bar one, so this
        // rejection is the one that ends the ceremony.
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out _,
            attemptCount: TwoFactorChallenge.MaxAttempts - 1);

        _totpServiceMock
            .Setup(s => s.ValidateCode(PlainSecret, "000000"))
            .Returns(false);

        // Act
        var result = await _handler.Handle(CreateCommand(code: "000000"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();

        _loginAttemptRepositoryMock.Verify(
            r => r.ResolveTwoFactorCeremonyAsync(
                It.IsAny<Guid>(), false, "Too many incorrect verification codes",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _loginAttemptRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── The commit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_LosingAConcurrentVerify_IssuesNothing()
    {
        // Two requests carrying the same still-valid code arrive together. Both
        // pass the in-memory validity check, because both read the same snapshot,
        // and both pass the code check. Only the database commit can separate
        // them, and the loser must walk away with no tokens rather than a second
        // session on one code.
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out var user, out _);

        _totpServiceMock
            .Setup(s => s.ValidateCode(PlainSecret, "123456"))
            .Returns(true);

        // Each request must load its OWN snapshot of the challenge, so that nothing
        // but the commit can tell them apart.
        var challengeId = Guid.NewGuid();
        _challengeRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(ChallengeTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TwoFactorChallenge(
                challengeId, userId, ChallengeTokenHash, "127.0.0.1",
                expiresAt: DateTime.UtcNow.AddMinutes(TwoFactorChallenge.DefaultLifetimeMinutes),
                usedAt: null, attemptCount: 0, createdAt: DateTime.UtcNow));
        _challengeRepositoryMock
            .Setup(r => r.TryReserveAttemptAsync(challengeId, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _stateStoreMock
            .SetupSequence(s => s.TryCommitLoginAsync(
                challengeId, userId, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed)
            .ReturnsAsync(LoginCommitOutcome.ChallengeLost);

        SetupBuild(user, CreateLoginResponse());

        var winner = await _handler.Handle(CreateCommand(), CancellationToken.None);
        var loser = await _handler.Handle(CreateCommand(), CancellationToken.None);

        winner.IsError.Should().BeFalse();
        loser.IsError.Should().BeTrue();
        loser.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");

        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<Guid?>()),
            Times.Once);
    }

    [Theory]
    [InlineData(LoginCommitOutcome.ChallengeLost)]
    [InlineData(LoginCommitOutcome.FactorLost)]
    public async Task Handle_CommitLost_IssuesNothing(LoginCommitOutcome outcome)
    {
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out var user, out var challenge);
        _totpServiceMock.Setup(s => s.ValidateCode(PlainSecret, "123456")).Returns(true);
        _stateStoreMock
            .Setup(s => s.TryCommitLoginAsync(
                challenge.Id, userId, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);
        SetupBuild(user, CreateLoginResponse());

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
        user.LastLoginAt.Should().BeNull("a sign-in that committed nothing is not recorded as a success");
        VerifyNothingIssued();
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ValidTotpCode_IssuesTokensAndConsumesChallenge()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out var user, out var challenge);
        var loginResponse = CreateLoginResponse();

        _totpServiceMock
            .Setup(s => s.ValidateCode(PlainSecret, "123456"))
            .Returns(true);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                user, "127.0.0.1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), challenge.Id))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(loginResponse);
        user.LastLoginAt.Should().NotBeNull();

        // One commit consumes the challenge and settles the factor (failures
        // cleared, LastUsedAt stamped) — in place of the whole-row write.
        _stateStoreMock.Verify(
            s => s.TryCommitLoginAsync(
                challenge.Id, userId,
                It.Is<SecondFactorProof>(proof => proof.Method == SecondFactorMethod.Totp),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Recovery codes ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidRecoveryCode_ConsumesCodeAndIssuesTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var storedHashes = new List<string> { "hash-1", "hash-2", "hash-3" };
        var stored = JsonSerializer.Serialize(storedHashes);
        SetupHappyPath(userId, out var user, out var challenge, recoveryCodes: stored);
        var loginResponse = CreateLoginResponse();

        _totpServiceMock
            .Setup(s => s.VerifyRecoveryCode("AAAA-BBBB", It.IsAny<string>()))
            .Returns<string, string>((_, hash) => hash == "hash-2");

        SecondFactorProof? committed = null;
        _stateStoreMock
            .Setup(s => s.TryCommitLoginAsync(
                challenge.Id, userId, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, SecondFactorProof, CancellationToken>((_, _, proof, _) => committed = proof)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        SetupBuild(user, loginResponse);

        // Act
        var result = await _handler.Handle(
            CreateCommand(code: "AAAA-BBBB", useRecoveryCode: true), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        committed.Should().NotBeNull();
        committed!.Method.Should().Be(SecondFactorMethod.RecoveryCode);
        committed.OldCodesJson.Should().Be(stored);
        var remaining = JsonSerializer.Deserialize<List<string>>(committed.NewCodesJson!);
        remaining.Should().BeEquivalentTo(new[] { "hash-1", "hash-3" });

        _stateStoreMock.Verify(
            s => s.TryCommitLoginAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidRecoveryCode_ReturnsInvalidRecoveryCode()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var storedHashes = new List<string> { "hash-1" };
        SetupHappyPath(userId, out _, out _,
            recoveryCodes: JsonSerializer.Serialize(storedHashes));

        _totpServiceMock
            .Setup(s => s.VerifyRecoveryCode(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

        // Act
        var result = await _handler.Handle(
            CreateCommand(code: "XXXX-YYYY", useRecoveryCode: true), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.InvalidRecoveryCode");
        _stateStoreMock.Verify(s => s.TryReserveAttemptAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        _stateStoreMock.Verify(
            s => s.TryCommitLoginAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoRecoveryCodesStored_ReturnsNoRecoveryCodesAvailable()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out _, recoveryCodes: null);

        // Act
        var result = await _handler.Handle(
            CreateCommand(code: "AAAA-BBBB", useRecoveryCode: true), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.NoRecoveryCodesAvailable");
    }

    [Fact]
    public async Task Handle_EmptyRecoveryCodeArray_ReturnsNoRecoveryCodesAvailable()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out _, recoveryCodes: "[]");

        // Act
        var result = await _handler.Handle(
            CreateCommand(code: "AAAA-BBBB", useRecoveryCode: true), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.NoRecoveryCodesAvailable");
    }
}
