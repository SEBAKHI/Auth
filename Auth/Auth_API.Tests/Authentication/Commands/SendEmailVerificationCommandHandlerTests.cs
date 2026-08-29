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
    private readonly Mock<IEnvironmentInfo> _environmentInfoMock;
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
        // Left without a Setup on purpose: Moq defaults the bool to false, which is
        // the production shape, so every existing test runs with the OTP log shut.
        _environmentInfoMock = new Mock<IEnvironmentInfo>();
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
            _environmentInfoMock.Object,
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

    [Fact]
    public async Task Handle_EmailDisabledOutsideDevelopment_DoesNotLogTheOtp()
    {
        // The code IS the credential: presenting it confirms ownership of the
        // address and completes verification with no further proof, and it is
        // written unmasked beside a masked address. Email:Enabled is a hot setting
        // an operator can flip from the console in production, so the environment
        // is the only thing keeping the code out of a production log. The mock
        // defaults IsDevelopment to false.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        _emailSettings.Enabled = false;
        SetupSuccessfulSendScenario(user);

        var result = await _handler.Handle(
            new SendEmailVerificationCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyOtpLogged(Times.Never());
    }

    [Fact]
    public async Task Handle_EmailDisabledInDevelopment_LogsTheOtp()
    {
        // The other half of the gate. With no mail server the log is the only place
        // the code exists, which is what makes the flow testable locally; a fix that
        // closed production by closing Development too would be a regression.
        _environmentInfoMock.Setup(e => e.IsDevelopment).Returns(true);
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "john@example.com", emailConfirmed: false);
        _emailSettings.Enabled = false;
        SetupSuccessfulSendScenario(user);

        var result = await _handler.Handle(
            new SendEmailVerificationCommand(userId), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyOtpLogged(Times.Once());
    }

    /// <summary>
    /// Matches the OTP itself rather than the level alone. This handler writes a
    /// second Warning - the rate-limit line - so a level-only assertion would stop
    /// meaning "the secret reached the log" the moment a test arranged a
    /// rate-limited request. Asserting on the value is also the claim the gate
    /// actually makes.
    /// </summary>
    private void VerifyOtpLogged(Times times) =>
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("123456")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

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
