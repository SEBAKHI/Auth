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
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordHistoryRepository> _passwordHistoryRepositoryMock;
    private readonly Mock<IUserSessionRepository> _userSessionRepositoryMock;
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
        _userSessionRepositoryMock = new Mock<IUserSessionRepository>();
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
            _userSessionRepositoryMock.Object,
            _passwordHasherMock.Object,
            _passwordValidator,
            TestHelpers.CreatePassingBreachEvaluator(),
            _eventDispatcherMock.Object,
            TestHelpers.CreateOptions(_passwordSettings),
            TestHelpers.CreateOptions(_sessionSettings),
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
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash))
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
            .Setup(h => h.VerifyPassword("WrongPass1!", user.PasswordHash))
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
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash))
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
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash))
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
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash))
            .Returns(true);

        _passwordHistoryRepositoryMock
            .Setup(r => r.GetRecentHashesAsync(userId, _passwordSettings.HistoryCount, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // NewPass1! matches current password hash
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
            .Setup(h => h.VerifyPassword("OldPass1!", originalPasswordHash))
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
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash))
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
    public async Task Handle_TerminateSessionsTrue_WithCurrentSessionId_TerminatesOtherSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: true, CurrentSessionId: currentSessionId);

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userSessionRepositoryMock.Verify(r => r.TerminateOtherSessionsAsync(
            userId, currentSessionId, "Password changed", It.IsAny<CancellationToken>()), Times.Once);
        _userSessionRepositoryMock.Verify(r => r.TerminateAllForUserAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TerminateSessionsTrue_WithoutCurrentSessionId_TerminatesAllSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: true);

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userSessionRepositoryMock.Verify(r => r.TerminateAllForUserAsync(
            userId, "Password changed", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TerminateSessionsFalse_DoesNotTerminateSessions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var command = new ChangePasswordCommand(userId, "OldPass1!", "NewPass1!", TerminateSessions: false);

        SetupSuccessfulPasswordChange(userId, user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _userSessionRepositoryMock.Verify(r => r.TerminateOtherSessionsAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _userSessionRepositoryMock.Verify(r => r.TerminateAllForUserAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
            .Setup(h => h.VerifyPassword("OldPass1!", user.PasswordHash))
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
}
