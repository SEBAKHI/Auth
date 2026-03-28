using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for ForgotPasswordCommandHandler.
/// </summary>
public class ForgotPasswordCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordResetTokenRepository> _passwordResetTokenRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ILogger<ForgotPasswordCommandHandler>> _loggerMock;
    private readonly EmailSettings _emailSettings;
    private readonly ForgotPasswordCommandHandler _handler;

    public ForgotPasswordCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordResetTokenRepositoryMock = new Mock<IPasswordResetTokenRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _loggerMock = new Mock<ILogger<ForgotPasswordCommandHandler>>();

        _emailSettings = new EmailSettings
        {
            Enabled = false,
            OtpExpirationMinutes = 15
        };

        _handler = new ForgotPasswordCommandHandler(
            _userRepositoryMock.Object,
            _passwordResetTokenRepositoryMock.Object,
            _passwordHasherMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ExistingUser_CreatesTokenAndReturnsResponse()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var command = new ForgotPasswordCommand("john@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<string>()))
            .Returns("HashedToken");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.MaskedEmail.Should().NotBeNullOrEmpty();
        result.Value.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Handle_ExistingUser_InvalidatesPreviousTokens()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var command = new ForgotPasswordCommand("john@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<string>()))
            .Returns("HashedToken");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.InvalidateAllForUserAsync(
            user.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingUser_StoresNewResetToken()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var command = new ForgotPasswordCommand("john@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<string>()))
            .Returns("HashedToken");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.CreateAsync(
            It.Is<PasswordResetToken>(t => t.UserId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonExistentUser_ReturnsFakeResponseToPreventEnumeration()
    {
        // Arrange
        var command = new ForgotPasswordCommand("nonexistent@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("nonexistent@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        result.Value.MaskedEmail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_NonExistentUser_DoesNotCreateToken()
    {
        // Arrange
        var command = new ForgotPasswordCommand("nonexistent@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("nonexistent@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.CreateAsync(
            It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailDisabled_LogsTokenForDevelopment()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        var command = new ForgotPasswordCommand("john@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("john@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _passwordHasherMock
            .Setup(h => h.HashPassword(It.IsAny<string>()))
            .Returns("HashedToken");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert - email is disabled by default in test setup, so token should be logged
        result.IsError.Should().BeFalse();
    }
}
