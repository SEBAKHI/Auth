using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ResetPassword;
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
/// Unit tests for ResetPasswordCommandHandler.
/// </summary>
public class ResetPasswordCommandHandlerTests
{
    private const string ValidToken = "valid-token";
    private const string ValidTokenHash = $"hmac({ValidToken})";

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordResetTokenRepository> _passwordResetTokenRepositoryMock;
    private readonly Mock<IPasswordHistoryRepository> _passwordHistoryRepositoryMock;
    private readonly Mock<IUserSessionRepository> _userSessionRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock;
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
        _userSessionRepositoryMock = new Mock<IUserSessionRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _tokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _loggerMock = new Mock<ILogger<ResetPasswordCommandHandler>>();

        _tokenKeyServiceMock.Setup(k => k.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string token) => $"hmac({token})");

        _passwordSettings = TestHelpers.CreatePasswordSettings();
        _sessionSettings = TestHelpers.CreateSessionSettings();

        _passwordValidator = new PasswordValidator(
            TestHelpers.CreateOptions(_passwordSettings));

        _handler = new ResetPasswordCommandHandler(
            _userRepositoryMock.Object,
            _passwordResetTokenRepositoryMock.Object,
            _passwordHistoryRepositoryMock.Object,
            _userSessionRepositoryMock.Object,
            _passwordHasherMock.Object,
            _tokenKeyServiceMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
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
    public async Task Handle_TerminateSessionsTrue_TerminatesAllSessions()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: true);

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userSessionRepositoryMock.Verify(r => r.TerminateAllForUserAsync(
            user.Id, "Password reset", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminateSessionsFalse_DoesNotTerminateSessions()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!", TerminateSessions: false);

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userSessionRepositoryMock.Verify(r => r.TerminateAllForUserAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ValidReset_SavesOldPasswordToHistory()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: ValidTokenHash);
        var command = new ResetPasswordCommand(ValidToken, "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordHistoryRepositoryMock.Verify(r => r.AddAsync(
            It.Is<PasswordHistory>(ph => ph.UserId == user.Id && ph.PasswordHash == user.PasswordHash),
            It.IsAny<CancellationToken>()), Times.Once);
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
