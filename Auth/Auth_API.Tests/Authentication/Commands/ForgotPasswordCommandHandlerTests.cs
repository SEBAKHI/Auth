using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
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
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IEnvironmentInfo> _environmentInfoMock;
    private readonly Mock<ILogger<ForgotPasswordCommandHandler>> _loggerMock;
    private readonly EmailSettings _emailSettings;
    private readonly ForgotPasswordCommandHandler _handler;

    public ForgotPasswordCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordResetTokenRepositoryMock = new Mock<IPasswordResetTokenRepository>();
        _tokenGeneratorMock = new Mock<ISecureTokenGenerator>();
        _tokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _notificationServiceMock = new Mock<INotificationService>();
        // Left without a Setup on purpose: Moq defaults the bool to false, which is
        // the production shape, so every existing test runs with the reset-link log
        // shut. Mirrors SecretChallengeTestContext.
        _environmentInfoMock = new Mock<IEnvironmentInfo>();
        _loggerMock = new Mock<ILogger<ForgotPasswordCommandHandler>>();

        _tokenGeneratorMock.Setup(g => g.Generate()).Returns(GeneratedToken);
        _tokenKeyServiceMock.Setup(k => k.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string token) => $"hmac({token})");

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

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
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            _environmentInfoMock.Object,
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
        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.TypeCode == NotificationTypeCodes.PasswordReset &&
                r.RecipientAddress == user.Email.Value &&
                ((string?)r.Variables["ResetLink"])!.Contains(GeneratedToken)),
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
        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r => Equals(r.Variables["ExpirationMinutes"], 45)),
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
        // Arrange - email is disabled in the test setup. The notification is still
        // dispatched and the response stays a generic success either way; whether
        // the reset link also reaches the log is a separate question, pinned by the
        // two tests below. See EmailSettingsTests for the URL shape.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EmailDisabledOutsideDevelopment_DoesNotLogTheResetLink()
    {
        // The link IS the credential: it resets the password with no further proof,
        // and it is written unmasked (Uri.EscapeDataString is an encoding, not a
        // redaction). Email:Enabled is a hot setting an operator can flip from the
        // console in production, so the environment is the only thing keeping the
        // link out of a production log. The mock defaults IsDevelopment to false.
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);

        await _handler.Handle(
            new ForgotPasswordCommand("john@example.com"), CancellationToken.None);

        VerifyResetLinkLogged(Times.Never());
    }

    [Fact]
    public async Task Handle_EmailDisabledInDevelopment_LogsTheResetLink()
    {
        // The other half of the gate. With no mail server the log is the only place
        // the link exists, which is what makes the flow testable locally; a fix that
        // closed production by closing Development too would be a regression.
        _environmentInfoMock.Setup(e => e.IsDevelopment).Returns(true);
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);

        await _handler.Handle(
            new ForgotPasswordCommand("john@example.com"), CancellationToken.None);

        VerifyResetLinkLogged(Times.Once());
    }

    /// <summary>
    /// The reset-link line is the only Warning this handler writes - everything else
    /// is Information or Error - so the level alone identifies it.
    /// </summary>
    private void VerifyResetLinkLogged(Times times) =>
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

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
        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.RecipientAddress == "john@example.com" &&
                !string.IsNullOrEmpty((string?)r.Variables["ResetLink"])),
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
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmailSendFails_StillReturnsGenericSuccess()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "john@example.com");
        ArrangeExistingUser(user);
        var command = new ForgotPasswordCommand("john@example.com");

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)NotificationErrors.SendFailed);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert - anti-enumeration: response stays a generic success
        result.IsError.Should().BeFalse();
        result.Value.MaskedEmail.Should().NotBeNullOrEmpty();
    }
}
