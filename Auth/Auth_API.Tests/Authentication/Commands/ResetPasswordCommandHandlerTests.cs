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
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordResetTokenRepository> _passwordResetTokenRepositoryMock;
    private readonly Mock<IPasswordHistoryRepository> _passwordHistoryRepositoryMock;
    private readonly Mock<IUserSessionRepository> _userSessionRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
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
        _loggerMock = new Mock<ILogger<ResetPasswordCommandHandler>>();

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
            _passwordValidator,
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidReset_UpdatesPasswordAndReturnsSuccess()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!");

        SetupValidResetScenario(user, resetToken, command);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _userRepositoryMock.Verify(r => r.UpdatePasswordAsync(
            user.Id, "NewHashedPassword", user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserNotFoundByEmail_ReturnsInvalidOrExpiredTokenError()
    {
        // Arrange
        var command = new ResetPasswordCommand("nonexistent@example.com", "token", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("nonexistent@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PasswordReset.InvalidOrExpiredToken");
    }

    [Fact]
    public async Task Handle_NoValidTokenFound_ReturnsInvalidOrExpiredTokenError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var command = new ResetPasswordCommand("john@example.com", "token", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetLatestValidTokenForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordResetToken?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("PasswordReset.InvalidOrExpiredToken");
    }

    [Fact]
    public async Task Handle_TokenHashMismatch_ReturnsInvalidOrExpiredTokenError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "wrong-token", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetLatestValidTokenForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("wrong-token", resetToken.TokenHash))
            .Returns(false);

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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "weak");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetLatestValidTokenForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("valid-token", resetToken.TokenHash))
            .Returns(true);

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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetLatestValidTokenForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("valid-token", resetToken.TokenHash))
            .Returns(true);

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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetLatestValidTokenForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("valid-token", resetToken.TokenHash))
            .Returns(true);

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(user.Id, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _passwordHasherMock
            .Setup(h => h.VerifyPassword("NewPass1!", user.PasswordHash))
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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!");

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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!", TerminateSessions: true);

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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!", TerminateSessions: false);

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
        var resetToken = TestHelpers.CreatePasswordResetToken(userId: user.Id, tokenHash: "HashedToken");
        var command = new ResetPasswordCommand("john@example.com", "valid-token", "NewPass1!");

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
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordResetTokenRepositoryMock
            .Setup(r => r.GetLatestValidTokenForUserAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resetToken);

        _passwordHasherMock
            .Setup(h => h.VerifyPassword(command.Token, resetToken.TokenHash))
            .Returns(true);

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
