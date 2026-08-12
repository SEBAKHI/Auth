using System.Text.Json;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.VerifyTwoFactorLogin;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for VerifyTwoFactorLoginCommandHandler.
/// </summary>
public class VerifyTwoFactorLoginCommandHandlerTests
{
    private const string ChallengeToken = "challenge-token";
    private const string ChallengeTokenHash = "challenge-token-hash";

    private readonly Mock<ITwoFactorChallengeRepository> _challengeRepositoryMock;
    private readonly Mock<ITwoFactorAuthRepository> _twoFactorRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ILoginAttemptRepository> _loginAttemptRepositoryMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<VerifyTwoFactorLoginCommandHandler>> _loggerMock;
    private readonly VerifyTwoFactorLoginCommandHandler _handler;

    public VerifyTwoFactorLoginCommandHandlerTests()
    {
        _challengeRepositoryMock = new Mock<ITwoFactorChallengeRepository>();
        _twoFactorRepositoryMock = new Mock<ITwoFactorAuthRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _loginAttemptRepositoryMock = new Mock<ILoginAttemptRepository>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _totpServiceMock = new Mock<ITotpService>();
        _loginResponseBuilderMock = new Mock<ILoginResponseBuilder>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<VerifyTwoFactorLoginCommandHandler>>();

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(ChallengeToken))
            .Returns(ChallengeTokenHash);

        _handler = new VerifyTwoFactorLoginCommandHandler(
            _challengeRepositoryMock.Object,
            _twoFactorRepositoryMock.Object,
            _userRepositoryMock.Object,
            _loginAttemptRepositoryMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _totpServiceMock.Object,
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

    private void SetupHappyPath(
        Guid userId,
        out User user,
        out TwoFactorAuth twoFactor,
        string? recoveryCodes = null,
        int attemptCount = 0)
    {
        // attemptCount lets a test start the challenge partway through its
        // allowance, which is the only way to reach the rejection that ends the
        // ceremony rather than merely counting against it.
        var challenge = new TwoFactorChallenge(
            Guid.NewGuid(),
            userId,
            ChallengeTokenHash,
            "127.0.0.1",
            expiresAt: DateTime.UtcNow.AddMinutes(TwoFactorChallenge.DefaultLifetimeMinutes),
            usedAt: null,
            attemptCount: attemptCount,
            createdAt: DateTime.UtcNow);
        SetupChallenge(challenge);

        // MarkAsUsedAsync returns whether THIS caller claimed the challenge. A
        // loose mock would answer false and turn every happy path here into
        // ChallengeInvalid, so the winning answer has to be stated.
        _challengeRepositoryMock
            .Setup(r => r.MarkAsUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        user = TestHelpers.CreateUser(id: userId, twoFactorEnabled: true);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId,
            isEnabled: true,
            secretKey: "TESTSECRET",
            recoveryCodes: recoveryCodes);
        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);
    }

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
    }

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

        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorAuth?)null);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.ChallengeInvalid");
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

        var twoFactor = TestHelpers.CreateTwoFactorAuth(
            userId: userId, isEnabled: true, lockedUntil: DateTime.UtcNow.AddMinutes(10));
        _twoFactorRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoFactor);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.LockedOut");

        _totpServiceMock.Verify(
            s => s.ValidateCode(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
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
    }

    [Fact]
    public async Task Handle_WrongTotpCode_RecordsFailureAndReturnsInvalidCode()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out var twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode(twoFactor.SecretKey, "000000"))
            .Returns(false);

        // Act
        var result = await _handler.Handle(CreateCommand(code: "000000"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");

        twoFactor.FailedAttempts.Should().Be(1);
        _challengeRepositoryMock.Verify(
            r => r.IncrementAttemptCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
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
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_LosingAConcurrentVerify_IssuesNothing()
    {
        // Two requests carrying the same still-valid code arrive together. Both
        // pass the in-memory validity check, because both read the same snapshot.
        // Only the database claim can separate them, and the loser must walk away
        // with no tokens rather than a second session on one code.
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out var user, out var twoFactor);

        _totpServiceMock
            .Setup(s => s.ValidateCode(twoFactor.SecretKey, "123456"))
            .Returns(true);

        // Each request must load its OWN snapshot of the challenge. Handing both
        // the same instance would let the entity's in-memory UsedAt reject the
        // second one, and the test would pass without the database claim doing
        // anything — which is precisely the illusion under test.
        var challengeId = Guid.NewGuid();
        _challengeRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(ChallengeTokenHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new TwoFactorChallenge(
                challengeId, userId, ChallengeTokenHash, "127.0.0.1",
                expiresAt: DateTime.UtcNow.AddMinutes(TwoFactorChallenge.DefaultLifetimeMinutes),
                usedAt: null, attemptCount: 0, createdAt: DateTime.UtcNow));

        _challengeRepositoryMock
            .SetupSequence(r => r.MarkAsUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                user, "127.0.0.1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(CreateLoginResponse());

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

    [Fact]
    public async Task Handle_LastAllowedWrongCode_SettlesTheCeremonyAsFailed()
    {
        // Arrange: the challenge has already burned its allowance bar one, so this
        // rejection is the one that ends the ceremony.
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out _, out var twoFactor,
            attemptCount: TwoFactorChallenge.MaxAttempts - 1);

        _totpServiceMock
            .Setup(s => s.ValidateCode(twoFactor.SecretKey, "000000"))
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

    [Fact]
    public async Task Handle_ValidTotpCode_IssuesTokensAndConsumesChallenge()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupHappyPath(userId, out var user, out var twoFactor);
        var loginResponse = CreateLoginResponse();

        _totpServiceMock
            .Setup(s => s.ValidateCode(twoFactor.SecretKey, "123456"))
            .Returns(true);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                user, "127.0.0.1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Should().Be(loginResponse);
        user.LastLoginAt.Should().NotBeNull();
        twoFactor.LastUsedAt.Should().NotBeNull();

        _challengeRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRecoveryCode_ConsumesCodeAndIssuesTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var storedHashes = new List<string> { "hash-1", "hash-2", "hash-3" };
        SetupHappyPath(userId, out var user, out var twoFactor,
            recoveryCodes: JsonSerializer.Serialize(storedHashes));
        var loginResponse = CreateLoginResponse();

        _totpServiceMock
            .Setup(s => s.VerifyRecoveryCode("AAAA-BBBB", It.IsAny<string>()))
            .Returns<string, string>((_, hash) => hash == "hash-2");

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                user, "127.0.0.1", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(loginResponse);

        // Act
        var result = await _handler.Handle(
            CreateCommand(code: "AAAA-BBBB", useRecoveryCode: true), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        var remaining = JsonSerializer.Deserialize<List<string>>(twoFactor.RecoveryCodes!);
        remaining.Should().BeEquivalentTo(new[] { "hash-1", "hash-3" });

        _twoFactorRepositoryMock.Verify(
            r => r.UpdateAsync(twoFactor, It.IsAny<CancellationToken>()),
            Times.Once);
        _challengeRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidRecoveryCode_ReturnsInvalidRecoveryCode()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var storedHashes = new List<string> { "hash-1" };
        SetupHappyPath(userId, out _, out var twoFactor,
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
        twoFactor.FailedAttempts.Should().Be(1);
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
