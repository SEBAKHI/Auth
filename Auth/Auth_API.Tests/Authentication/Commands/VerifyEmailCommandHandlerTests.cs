using Auth.Application.DTOs;
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
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock;
    private readonly Mock<ITwoFactorChallengeService> _twoFactorChallengeServiceMock;
    private readonly Mock<IDomainEventDispatcher> _eventDispatcherMock;
    private readonly Mock<ILogger<VerifyEmailCommandHandler>> _loggerMock;
    private readonly VerifyEmailCommandHandler _handler;

    public VerifyEmailCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenRepositoryMock = new Mock<IEmailVerificationTokenRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _loginResponseBuilderMock = new Mock<ILoginResponseBuilder>();
        _twoFactorChallengeServiceMock = new Mock<ITwoFactorChallengeService>();
        _eventDispatcherMock = new Mock<IDomainEventDispatcher>();
        _loggerMock = new Mock<ILogger<VerifyEmailCommandHandler>>();

        _handler = new VerifyEmailCommandHandler(
            _userRepositoryMock.Object,
            _tokenRepositoryMock.Object,
            _passwordHasherMock.Object,
            _loginResponseBuilderMock.Object,
            _twoFactorChallengeServiceMock.Object,
            _eventDispatcherMock.Object,
            _loggerMock.Object);
    }

    private static LoginResponse CreateLoginResponse() => new()
    {
        Token = new TokenResponse
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            ExpiresIn = 900,
            RefreshExpiresIn = 604800
        },
        User = new UserInfo
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            FirstName = "Test",
            LastName = "User"
        }
    };

    private void SetupTokenBuilder()
    {
        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLoginResponse());
    }

    [Fact]
    public async Task Handle_ValidOtpByUserId_ConfirmsEmailWithoutIssuingTokens()
    {
        // Arrange: the admin (user-id-keyed) path must confirm only — an admin
        // verifying another user must never receive that user's tokens.
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
        result.Value.Login.Should().BeNull();
        _tokenRepositoryMock.Verify(
            r => r.MarkAsUsedAsync(token.Id, It.IsAny<CancellationToken>()),
            Times.Once());
        _userRepositoryMock.Verify(
            r => r.ConfirmEmailAsync(userId, userId, It.IsAny<CancellationToken>()),
            Times.Once());
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_ValidOtpByEmail_ConfirmsEmailAndSignsUserIn()
    {
        // Arrange: the anonymous (email-keyed) self-service path confirms the
        // address and issues a session in one step.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: false);
        var token = TestHelpers.CreateEmailVerificationToken(userId: userId);
        var command = new VerifyEmailCommand(null, "123456", user.Email, "127.0.0.1", "TestAgent/1.0");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetValidTokenForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("123456", token.OtpHash))
            .Returns(true);
        SetupTokenBuilder();

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Login.Should().NotBeNull();
        result.Value.Login!.Token.Should().NotBeNull();
        _userRepositoryMock.Verify(
            r => r.ConfirmEmailAsync(userId, userId, It.IsAny<CancellationToken>()),
            Times.Once());
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(user, "127.0.0.1", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _eventDispatcherMock.Verify(
            d => d.DispatchEventsAsync(user, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ValidOtpByEmail_WithTwoFactorEnabled_ReturnsChallengeInsteadOfTokens()
    {
        // Arrange: defensive parity with login — if the account has two-factor
        // enabled we hand back a pending-2FA response, never tokens.
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, emailConfirmed: false, twoFactorEnabled: true);
        var token = TestHelpers.CreateEmailVerificationToken(userId: userId);
        var command = new VerifyEmailCommand(null, "123456", user.Email, "127.0.0.1", "TestAgent/1.0");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _tokenRepositoryMock
            .Setup(r => r.GetValidTokenForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("123456", token.OtpHash))
            .Returns(true);
        _twoFactorChallengeServiceMock
            .Setup(s => s.CreateChallengeAsync(user, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("challenge-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Login.Should().NotBeNull();
        result.Value.Login!.RequiresTwoFactor.Should().BeTrue();
        result.Value.Login!.TwoFactorChallengeToken.Should().Be("challenge-token");
        result.Value.Login!.Token.Should().BeNull();
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
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

    [Fact]
    public async Task Handle_UnknownEmail_ReturnsInvalidOtpNotUserNotFound()
    {
        // Arrange: unknown email must not reveal account existence.
        var command = new VerifyEmailCommand(null, "123456", "unknown@example.com");

        _userRepositoryMock
            .Setup(r => r.GetByEmailAsync("unknown@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
    }
}
