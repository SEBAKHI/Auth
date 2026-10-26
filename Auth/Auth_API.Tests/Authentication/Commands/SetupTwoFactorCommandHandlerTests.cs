using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SetupTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for SetupTwoFactorCommandHandler.
/// </summary>
public class SetupTwoFactorCommandHandlerTests
{
    private const string ProtectedSecret = "v2:protected-new-secret";
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IReauthenticationGuard> _guardMock = new();
    private readonly Mock<ITwoFactorStateStore> _stateStoreMock = new();
    private readonly Mock<ITwoFactorSecretProtector> _secretProtectorMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPlatformSettingsRepository> _platformSettingsRepositoryMock;
    private readonly Mock<ITotpService> _totpServiceMock;
    private readonly Mock<ILogger<SetupTwoFactorCommandHandler>> _loggerMock;
    private readonly TwoFactorSettings _twoFactorSettings = new();
    private readonly EmailSettings _emailSettings = new();
    private readonly SetupTwoFactorCommandHandler _handler;

    public SetupTwoFactorCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _platformSettingsRepositoryMock = new Mock<IPlatformSettingsRepository>();
        _totpServiceMock = new Mock<ITotpService>();
        _loggerMock = new Mock<ILogger<SetupTwoFactorCommandHandler>>();

        // Every test runs from a recent session; the guard has its own tests, and
        // TwoFactorLifecycleGuardTests holds every lifecycle handler to it.
        _guardMock
            .Setup(g => g.EnsureRecentSignInAsync(It.IsAny<Guid>(), SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, "Chrome on Windows"));
        _secretProtectorMock
            .Setup(p => p.ProtectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProtectedSecret);
        _stateStoreMock
            .Setup(s => s.TryStorePendingSecretAsync(It.IsAny<Guid>(), ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var jwtSettings = TestHelpers.CreateOptions(new JwtSettings
        {
            Issuer = "TestIssuer"
        });

        _handler = CreateHandler(jwtSettings);
    }

    private SetupTwoFactorCommandHandler CreateHandler(IOptionsSnapshot<JwtSettings> jwtSettings) =>
        new(
            _guardMock.Object,
            _userRepositoryMock.Object,
            _stateStoreMock.Object,
            _secretProtectorMock.Object,
            new AuthenticatorKeyFactory(
                _platformSettingsRepositoryMock.Object,
                _totpServiceMock.Object,
                jwtSettings,
                Mock.Of<ILogger<AuthenticatorKeyFactory>>()),
            new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_emailSettings)),
            TestHelpers.CreateSecondFactorLockout(),
            _loggerMock.Object);

    private void GivenPlatformName(string platformName) =>
        _platformSettingsRepositoryMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformSettings(
                PlatformSettings.SingletonId, platformName, null, null, null, null, null));

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId, SessionId);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Handle_TwoFactorAlreadyEnabled_ReturnsConflictError_WithoutTheSecret()
    {
        // The store found an enabled factor: nothing was written, and the secret
        // generated for this request is never handed out.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        GivenSetupCanProceed(userId, user, "ABCDEFGHIJKLMNOP");
        _stateStoreMock
            .Setup(s => s.TryStorePendingSecretAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(UserErrors.TwoFactorAlreadyEnabled.Code);
    }

    /// <remarks>
    /// X12 PR B: a pending factor at the consecutive-failure limit accepts no code,
    /// and rotating it would move its change stamp past the owner's password reset,
    /// closing again the administrator's gate. Below the limit setup is unchanged.
    /// Deliberate break: drop the check.
    /// </remarks>
    [Theory]
    [InlineData(100, false, true)]
    [InlineData(99, false, false)]
    [InlineData(100, true, false)]
    public async Task Handle_PendingFactorAtTheLimit_IsNotRotated(int failedAttempts, bool isEnabled, bool refused)
    {
        var userId = Guid.NewGuid();
        GivenSetupCanProceed(userId, TestHelpers.CreateUser(id: userId, email: "test@example.com"), "ABCDEFGHIJKLMNOP");
        _stateStoreMock
            .Setup(s => s.GetSnapshotAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Auth.Domain.ValueObjects.TwoFactorSnapshot(
                userId, "v2:secret", null, isEnabled, failedAttempts, null, modifiedAt: DateTime.UtcNow));

        var result = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        if (refused)
        {
            result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code);
            _stateStoreMock.Verify(
                s => s.TryStorePendingSecretAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _secretProtectorMock.Verify(
                p => p.ProtectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never, "no secret is generated for a factor that cannot be enabled");
        }
        else
        {
            _stateStoreMock.Verify(
                s => s.TryStorePendingSecretAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()),
                Times.Once, "below the limit, or for a factor in use, setup goes on as before");
        }
    }

    [Fact]
    public async Task Handle_NoPriorSetup_StoresTheEncryptedSecretAndReturnsSetupResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId, SessionId);
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        var secret = "ABCDEFGHIJKLMNOP";
        var qrCodeUri = "otpauth://totp/TestIssuer:test@example.com?secret=ABCDEFGHIJKLMNOP&issuer=TestIssuer";

        GivenSetupCanProceed(userId, user, secret);

        _totpServiceMock
            .Setup(s => s.GenerateQrCodeUri(secret, user.Email, "TestIssuer"))
            .Returns(qrCodeUri);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Secret.Should().Be(secret);
        result.Value.QrCodeUri.Should().Be(qrCodeUri);
        result.Value.ManualEntryKey.Should().Be("ABCD EFGH IJKL MNOP");

        _secretProtectorMock.Verify(p => p.ProtectAsync(userId, secret, It.IsAny<CancellationToken>()), Times.Once);
        _stateStoreMock.Verify(
            s => s.TryStorePendingSecretAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Setup_RotatesInPlace_WithoutDelete()
    {
        // Setup never removes and re-creates the factor row: that cleared the
        // failure count and the lock guessing at enable had earned. It stores the
        // new secret through the one conditional write that rotates a pending row
        // in place (or inserts the first one), and has no way to reach the
        // whole-row repository that could delete it. That repository is gone (X12
        // PR B: it had no consumer, and its DeleteAsync and UpdateAsync bypassed
        // the reservation), so the guard is that it stays gone.
        typeof(ITwoFactorStateStore).Assembly
            .GetType("Auth.Domain.Interfaces.Repositories.ITwoFactorAuthRepository")
            .Should().BeNull("the factor row changes only through the conditional statements of ITwoFactorStateStore");
        typeof(SetupTwoFactorCommandHandler).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.Name)
            .Should().NotContain(name => name.Contains("TwoFactorAuthRepository"),
                "setup changes the factor only through ITwoFactorStateStore");

        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        GivenSetupCanProceed(userId, user, "NEWBASE32SECRET1");

        var stored = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        stored.IsError.Should().BeFalse();
        _stateStoreMock.Verify(
            s => s.TryStorePendingSecretAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()),
            Times.Once);

        // Nothing rotated (the factor is enabled): AlreadyEnabled, never a second write.
        _stateStoreMock
            .Setup(s => s.TryStorePendingSecretAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var refused = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        refused.FirstError.Code.Should().Be(UserErrors.TwoFactorAlreadyEnabled.Code);
        _stateStoreMock.Verify(
            s => s.TryStorePendingSecretAsync(userId, ProtectedSecret, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_StaleSession_GeneratesNoSecret()
    {
        var userId = Guid.NewGuid();
        GivenSetupCanProceed(userId, TestHelpers.CreateUser(id: userId), "ABCDEFGHIJKLMNOP");
        _guardMock
            .Setup(g => g.EnsureRecentSignInAsync(userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        var result = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _totpServiceMock.Verify(s => s.GenerateSecret(), Times.Never);
        _stateStoreMock.Verify(
            s => s.TryStorePendingSecretAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NullIssuer_UsesDefaultIssuer()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SetupTwoFactorCommand(userId, SessionId);
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        var secret = "ABCDEFGHIJKLMNOP";

        var jwtSettingsNoIssuer = TestHelpers.CreateOptions(new JwtSettings { Issuer = null! });
        var handler = CreateHandler(jwtSettingsNoIssuer);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);


        _totpServiceMock
            .Setup(s => s.GenerateSecret())
            .Returns(secret);

        _totpServiceMock
            .Setup(s => s.GenerateQrCodeUri(secret, user.Email, "AuthSystem"))
            .Returns("otpauth://totp/AuthSystem:test@example.com");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        _totpServiceMock.Verify(
            s => s.GenerateQrCodeUri(secret, user.Email, "AuthSystem"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlatformNameConfigured_UsesItAsTheIssuer()
    {
        // The issuer is what the authenticator app lists as the account's
        // provider, so it has to be the platform's display name, not a URL.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        GivenSetupCanProceed(userId, user, "ABCDEFGHIJKLMNOP");
        GivenPlatformName("  YourBrand Console  ");

        var result = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _totpServiceMock.Verify(
            s => s.GenerateQrCodeUri(It.IsAny<string>(), user.Email, "YourBrand Console"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_NoPlatformNameAndUrlIssuer_FallsBackToTheHost()
    {
        // Jwt:Issuer is a URL in every deployed environment. Passing it whole
        // put "https://auth.example.com" — percent-encoded — in the app's
        // account list, and the encoded "://" inside the otpauth label trips
        // stricter parsers.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        GivenSetupCanProceed(userId, user, "ABCDEFGHIJKLMNOP");

        var handler = CreateHandler(TestHelpers.CreateOptions(
            new JwtSettings { Issuer = "https://auth.example.com" }));

        var result = await handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _totpServiceMock.Verify(
            s => s.GenerateQrCodeUri(It.IsAny<string>(), user.Email, "auth.example.com"),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PlatformSettingsUnavailable_StillCompletesSetup()
    {
        // Branding is not worth failing an enrolment over.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "test@example.com");
        GivenSetupCanProceed(userId, user, "ABCDEFGHIJKLMNOP");

        _platformSettingsRepositoryMock
            .Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var result = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _totpServiceMock.Verify(
            s => s.GenerateQrCodeUri(It.IsAny<string>(), user.Email, "TestIssuer"),
            Times.Once);
    }

    // ── X02 PR B: whether enable will want the emailed code ─────────────────

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Handle_SetsEmailCodeRequired_FromEmail(bool emailEnabled, bool expected)
    {
        // Setup only ever reaches a pending factor, so this is the account's first:
        // enable will want the code exactly while email is on. A client that reads
        // false skips the step, and enable then needs none.
        _emailSettings.Enabled = emailEnabled;
        var userId = Guid.NewGuid();
        GivenSetupCanProceed(userId, TestHelpers.CreateUser(id: userId, email: "test@example.com"), "ABCDEFGHIJKLMNOP");

        var result = await _handler.Handle(new SetupTwoFactorCommand(userId, SessionId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.EmailCodeRequired.Should().Be(expected);
    }

    private void GivenSetupCanProceed(Guid userId, Auth.Domain.Entities.User user, string secret)
    {
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);


        _totpServiceMock.Setup(s => s.GenerateSecret()).Returns(secret);
    }
}
