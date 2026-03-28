using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ResendEmailVerification;
using Auth.Application.Interfaces;
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
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IOtpGenerator> _otpGeneratorMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ILogger<ResendEmailVerificationCommandHandler>> _loggerMock;
    private readonly EmailSettings _emailSettings;
    private readonly ResendEmailVerificationCommandHandler _handler;

    public ResendEmailVerificationCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenRepositoryMock = new Mock<IEmailVerificationTokenRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        _otpGeneratorMock = new Mock<IOtpGenerator>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
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
            _emailServiceMock.Object,
            _otpGeneratorMock.Object,
            _passwordHasherMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
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
        _emailServiceMock
            .Setup(s => s.SendVerificationOtpAsync(user.Email, It.IsAny<string>(), "123456", _emailSettings.OtpExpirationMinutes, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
        _emailServiceMock.Verify(
            s => s.SendVerificationOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
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
        _emailServiceMock
            .Setup(s => s.SendVerificationOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.EmailSendFailed.Code);
    }
}
