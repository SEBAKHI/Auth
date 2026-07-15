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
    private const string GeneratedToken = "generated-reset-token";

    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordResetTokenRepository> _passwordResetTokenRepositoryMock;
    private readonly Mock<ISecureTokenGenerator> _tokenGeneratorMock;
    private readonly Mock<IRefreshTokenKeyService> _tokenKeyServiceMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<ILogger<ForgotPasswordCommandHandler>> _loggerMock;
    private readonly EmailSettings _emailSettings;
    private readonly ForgotPasswordCommandHandler _handler;

    public ForgotPasswordCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordResetTokenRepositoryMock = new Mock<IPasswordResetTokenRepository>();
        _tokenGeneratorMock = new Mock<ISecureTokenGenerator>();
        _tokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _emailServiceMock = new Mock<IEmailService>();
        _loggerMock = new Mock<ILogger<ForgotPasswordCommandHandler>>();

        _tokenGeneratorMock.Setup(g => g.Generate()).Returns(GeneratedToken);
        _tokenKeyServiceMock.Setup(k => k.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string token) => $"hmac({token})");

        _emailServiceMock
            .Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _emailSettings = new EmailSettings
        {
            Enabled = false,
            OtpExpirationMinutes = 15,
            ResetTokenExpirationMinutes = 30,
            FrontendBaseUrl = "https://accounts.example.com"
        };

        _handler = new ForgotPasswordCommandHandler(
            _userRepositoryMock.Object,
            _passwordResetTokenRepositoryMock.Object,
            _tokenGeneratorMock.Object,
            _tokenKeyServiceMock.Object,
            _emailServiceMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            _loggerMock.Object);
    }

    private void ArrangeExistingUser(User user) =>
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

    private void ArrangeMissingUser(string email) =>
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

    [Fact]
    public async Task Handle_ExistingUser_CreatesTokenAndReturnsResponse()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

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
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

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
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.CreateAsync(
            It.Is<PasswordResetToken>(t => t.UserId == user.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingUser_StoresDeterministicHashOfTheEmailedToken()
    {
        // Arrange - the stored hash must be the HMAC of the token the user receives,
        // otherwise redemption (which looks the token up by hash) cannot find it.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _tokenKeyServiceMock.Verify(k => k.ComputeTokenHash(GeneratedToken), Times.Once);
        _passwordResetTokenRepositoryMock.Verify(r => r.CreateAsync(
            It.Is<PasswordResetToken>(t => t.TokenHash == $"hmac({GeneratedToken})"),
            It.IsAny<CancellationToken>()), Times.Once);
        _emailServiceMock.Verify(e => e.SendPasswordResetAsync(
            user.Email, It.IsAny<string>(), GeneratedToken, It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ExistingUser_UsesConfiguredExpiration()
    {
        // Arrange
        _emailSettings.ResetTokenExpirationMinutes = 45;
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Value.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(45), TimeSpan.FromMinutes(1));
        _emailServiceMock.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), 45,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonExistentUser_ReturnsFakeResponseToPreventEnumeration()
    {
        // Arrange
        ArrangeMissingUser("nonexistent@example.com");
        var command = new ForgotPasswordCommand("nonexistent@example.com");

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
        ArrangeMissingUser("nonexistent@example.com");
        var command = new ForgotPasswordCommand("nonexistent@example.com");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _passwordResetTokenRepositoryMock.Verify(r => r.CreateAsync(
            It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailDisabled_StillSucceeds()
    {
        // Arrange - email is disabled in the test setup, so the handler logs the
        // reset link instead of sending it. See EmailSettingsTests for the URL shape.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ExistingUser_SendsPasswordResetEmail()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _emailServiceMock.Verify(e => e.SendPasswordResetAsync(
            "john@example.com",
            It.IsAny<string>(),
            It.Is<string>(token => !string.IsNullOrEmpty(token)),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NonExistentUser_DoesNotSendEmail()
    {
        // Arrange
        ArrangeMissingUser("nonexistent@example.com");
        var command = new ForgotPasswordCommand("nonexistent@example.com");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _emailServiceMock.Verify(e => e.SendPasswordResetAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EmailSendFails_StillReturnsGenericSuccess()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        _emailServiceMock
            .Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert - anti-enumeration: response stays a generic success
        result.IsError.Should().BeFalse();
        result.Value.MaskedEmail.Should().NotBeNullOrEmpty();
    }
}
