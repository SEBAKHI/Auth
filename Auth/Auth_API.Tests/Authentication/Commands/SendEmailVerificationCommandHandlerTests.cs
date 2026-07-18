using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.SendEmailVerification;
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
/// Unit tests for SendEmailVerificationCommandHandler.
/// </summary>
public class SendEmailVerificationCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IEmailVerificationTokenRepository> _tokenRepositoryMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IOtpGenerator> _otpGeneratorMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ILogger<SendEmailVerificationCommandHandler>> _loggerMock;
    private readonly EmailSettings _emailSettings;
    private readonly SendEmailVerificationCommandHandler _handler;

    public SendEmailVerificationCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenRepositoryMock = new Mock<IEmailVerificationTokenRepository>();
        _notificationServiceMock = new Mock<INotificationService>();
        _otpGeneratorMock = new Mock<IOtpGenerator>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _loggerMock = new Mock<ILogger<SendEmailVerificationCommandHandler>>();

        _emailSettings = new EmailSettings
        {
            Enabled = true,
            OtpExpirationMinutes = 15,
            RateLimitWindowSeconds = 60,
            MaxOtpRequestsPerWindow = 3
        };

        _handler = new SendEmailVerificationCommandHandler(
            _userRepositoryMock.Object,
            _tokenRepositoryMock.Object,
            _notificationServiceMock.Object,
            _otpGeneratorMock.Object,
            _passwordHasherMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_SendsOtpAndReturnsResponse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        var command = new SendEmailVerificationCommand(userId);

        SetupSuccessfulSendScenario(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        result.Value.MaskedEmail.Should().NotBeNullOrEmpty();

        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.TypeCode == NotificationTypeCodes.EmailVerification &&
                r.RecipientUserId == userId &&
                Equals(r.Variables["OtpCode"], "123456") &&
                Equals(r.Variables["ExpirationMinutes"], _emailSettings.OtpExpirationMinutes)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new SendEmailVerificationCommand(userId);

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
    public async Task Handle_EmailAlreadyVerified_ReturnsAlreadyVerifiedError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: true);
        var command = new SendEmailVerificationCommand(userId);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("EmailVerification.EmailAlreadyVerified");
    }

    [Fact]
    public async Task Handle_RateLimitExceeded_ReturnsTooManyRequestsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        var command = new SendEmailVerificationCommand(userId);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_emailSettings.MaxOtpRequestsPerWindow);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("EmailVerification.TooManyRequests");
    }

    [Fact]
    public async Task Handle_ValidRequest_InvalidatesExistingTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        var command = new SendEmailVerificationCommand(userId);

        SetupSuccessfulSendScenario(user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _tokenRepositoryMock.Verify(r => r.InvalidateAllForUserAsync(
            userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesAndStoresToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        var command = new SendEmailVerificationCommand(userId);

        SetupSuccessfulSendScenario(user);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _tokenRepositoryMock.Verify(r => r.CreateAsync(
            It.Is<EmailVerificationToken>(t => t.UserId == userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmailSendFails_ReturnsEmailSendFailedError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        var command = new SendEmailVerificationCommand(userId);

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _otpGeneratorMock
            .Setup(g => g.GenerateNumericOtp(6))
            .Returns("123456");

        _passwordHasherMock
            .Setup(h => h.HashPassword("123456"))
            .Returns("HashedOtp");

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)NotificationErrors.SendFailed);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("EmailVerification.EmailSendFailed");
    }

    private void SetupSuccessfulSendScenario(User user)
    {
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _otpGeneratorMock
            .Setup(g => g.GenerateNumericOtp(6))
            .Returns("123456");

        _passwordHasherMock
            .Setup(h => h.HashPassword("123456"))
            .Returns("HashedOtp");

        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
    }
}
