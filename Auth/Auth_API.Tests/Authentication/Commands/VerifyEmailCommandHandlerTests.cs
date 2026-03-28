using Auth.Application.Features.Authentication.VerifyEmail;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

public class VerifyEmailCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IEmailVerificationTokenRepository> _tokenRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ILogger<VerifyEmailCommandHandler>> _loggerMock;
    private readonly VerifyEmailCommandHandler _handler;

    public VerifyEmailCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenRepositoryMock = new Mock<IEmailVerificationTokenRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _loggerMock = new Mock<ILogger<VerifyEmailCommandHandler>>();

        _handler = new VerifyEmailCommandHandler(
            _userRepositoryMock.Object,
            _tokenRepositoryMock.Object,
            _passwordHasherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidOtp_ConfirmsEmailSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: false);
        var token = TestHelpers.CreateEmailVerificationToken(userId: userId);
        var command = new VerifyEmailCommand(userId, "123456");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetValidTokenForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("123456", token.OtpHash))
            .Returns(true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _tokenRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(token.Id, It.IsAny<CancellationToken>()),
            Times.Once());
        _userRepositoryMock.Verify(
            r => r.ConfirmEmailAsync(userId, userId, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_InvalidOtpFormat_ReturnsError()
    {
        // Arrange
        var command = new VerifyEmailCommand(Guid.NewGuid(), "abc");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOtpFormat.Code);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new VerifyEmailCommand(userId, "123456");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.UserNotFound.Code);
    }

    [Fact]
    public async Task Handle_EmailAlreadyVerified_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: true);
        var command = new VerifyEmailCommand(userId, "123456");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.EmailAlreadyVerified.Code);
    }

    [Fact]
    public async Task Handle_NoValidToken_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: false);
        var command = new VerifyEmailCommand(userId, "123456");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetValidTokenForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmailVerificationToken?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
    }

    [Fact]
    public async Task Handle_MaxAttemptsExceeded_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: false);
        var token = TestHelpers.CreateEmailVerificationToken(
            userId: userId,
            attemptCount: EmailVerificationToken.MaxAttempts);
        var command = new VerifyEmailCommand(userId, "123456");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetValidTokenForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.TooManyAttempts.Code);
    }

    [Fact]
    public async Task Handle_InvalidOtp_IncrementsAttemptCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: false);
        var token = TestHelpers.CreateEmailVerificationToken(userId: userId, attemptCount: 0);
        var command = new VerifyEmailCommand(userId, "999999");

        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetValidTokenForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("999999", token.OtpHash))
            .Returns(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        _tokenRepositoryMock.Verify(
            r => r.IncrementAttemptCountAsync(token.Id, It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
