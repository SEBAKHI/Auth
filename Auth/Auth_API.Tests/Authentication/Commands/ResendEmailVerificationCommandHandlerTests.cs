using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ResendEmailVerification;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

public class ResendEmailVerificationCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IEmailVerificationTokenRepository> _tokenRepositoryMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IOtpGenerator> _otpGeneratorMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IEnvironmentInfo> _environmentInfoMock;
    private readonly Mock<ILogger<ResendEmailVerificationCommandHandler>> _loggerMock;
    private readonly EmailSettings _emailSettings;
    private readonly ResendEmailVerificationCommandHandler _handler;

    public ResendEmailVerificationCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenRepositoryMock = new Mock<IEmailVerificationTokenRepository>();
        _notificationServiceMock = new Mock<INotificationService>();
        _otpGeneratorMock = new Mock<IOtpGenerator>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        // Left without a Setup on purpose: Moq defaults the bool to false, which is
        // the production shape, so every existing test runs with the OTP log shut.
        _environmentInfoMock = new Mock<IEnvironmentInfo>();
        _loggerMock = new Mock<ILogger<ResendEmailVerificationCommandHandler>>();

        _emailSettings = new EmailSettings
        {
            OtpExpirationMinutes = 15,
            MaxOtpRequestsPerWindow = 3,
            RateLimitWindowSeconds = 60,
            Enabled = true
        };

        _handler = new ResendEmailVerificationCommandHandler(
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
    public async Task Handle_ValidRequest_SendsVerificationEmail()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "test@example.com", emailConfirmed: false);
        var command = new ResendEmailVerificationCommand("test@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _otpGeneratorMock
            .Setup(g => g.GenerateNumericOtp(6))
            .Returns("123456");
        _passwordHasherMock
            .Setup(h => h.HashPassword("123456"))
            .Returns("hashed-otp");
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.MaskedEmail.Should().NotBeNullOrEmpty();
        _tokenRepositoryMock.Verify(
            r => r.InvalidateAllForUserAsync(user.Id, It.IsAny<CancellationToken>()),
            Times.Once());
        _tokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<EmailVerificationToken>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NonExistentEmail_ReturnsFakeResponseToPreventEnumeration()
    {
        // Arrange
        var command = new ResendEmailVerificationCommand("nonexistent@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _notificationServiceMock.Verify(
            s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_AlreadyVerifiedEmail_ReturnsError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "verified@example.com", emailConfirmed: true);
        var command = new ResendEmailVerificationCommand("verified@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.EmailAlreadyVerified.Code);
    }

    [Fact]
    public async Task Handle_RateLimitExceeded_ReturnsError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "test@example.com", emailConfirmed: false);
        var command = new ResendEmailVerificationCommand("test@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_emailSettings.MaxOtpRequestsPerWindow);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.TooManyRequests.Code);
    }

    [Fact]
    public async Task Handle_EmailSendFails_ReturnsError()
    {
        // Arrange
        var user = TestHelpers.CreateUser(email: "test@example.com", emailConfirmed: false);
        var command = new ResendEmailVerificationCommand("test@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _otpGeneratorMock
            .Setup(g => g.GenerateNumericOtp(6))
            .Returns("123456");
        _passwordHasherMock
            .Setup(h => h.HashPassword("123456"))
            .Returns("hashed-otp");
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ErrorOr<Success>)NotificationErrors.SendFailed);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.EmailSendFailed.Code);
    }

    private const string LoggedOtp = "123456";

    /// <summary>
    /// Arranges the single path that reaches the OTP log line: an existing,
    /// unverified user, under the rate limit, with mail turned off. Deliberately
    /// avoids the two early-return branches so the only Warning the handler can
    /// write on this path is the one under test.
    /// </summary>
    private void ArrangeOtpLogPath()
    {
        // This class enables email in its setup, unlike the forgot-password one.
        _emailSettings.Enabled = false;

        var user = TestHelpers.CreateUser(email: "test@example.com", emailConfirmed: false);
        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("test@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetRecentTokenCountAsync(user.Email, _emailSettings.RateLimitWindow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _otpGeneratorMock
            .Setup(g => g.GenerateNumericOtp(6))
            .Returns(LoggedOtp);
        _passwordHasherMock
            .Setup(h => h.HashPassword(LoggedOtp))
            .Returns("hashed-otp");
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
    }

    [Fact]
    public async Task Handle_EmailDisabledOutsideDevelopment_DoesNotLogTheOtp()
    {
        // The code IS the credential: presenting it confirms ownership of the
        // address, and it is written unmasked - the address beside it is masked,
        // the code is not. Email:Enabled is a hot setting an operator can flip from
        // the console in production, so the environment is the only thing keeping a
        // live code out of a production log. The mock defaults IsDevelopment to
        // false.
        ArrangeOtpLogPath();

        var result = await _handler.Handle(
            new ResendEmailVerificationCommand("test@example.com"), CancellationToken.None);

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
        ArrangeOtpLogPath();

        var result = await _handler.Handle(
            new ResendEmailVerificationCommand("test@example.com"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyOtpLogged(Times.Once());
    }

    /// <summary>
    /// Matches the message, not the level alone: this handler writes two other
    /// Warnings - the unknown-email and rate-limit lines - so a level-only
    /// assertion would be pinned to the arrangement rather than to the line under
    /// test. The substring carries the OTP itself, which is the thing that must not
    /// reach the log.
    /// </summary>
    private void VerifyOtpLogged(Times times) =>
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains($": {LoggedOtp} (expires in")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
}
