using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Logout;
using Auth.Application.Interfaces;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

public class LogoutCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<ITokenBlacklistService> _tokenBlacklistServiceMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock;
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock;
    private readonly Mock<ILogger<LogoutCommandHandler>> _loggerMock;
    private readonly JwtSettings _jwtSettings;
    private readonly LogoutCommandHandler _handler;

    public LogoutCommandHandlerTests()
    {
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _tokenBlacklistServiceMock = new Mock<ITokenBlacklistService>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _publisherMock = new Mock<IPublisher>();
        _credentialRevocationMock = new Mock<ICredentialRevocationService>();
        _sessionRepositoryMock = new Mock<IUserSessionRepository>();
        _loggerMock = new Mock<ILogger<LogoutCommandHandler>>();

        _jwtSettings = new JwtSettings
        {
            AccessTokenLifetimeMinutes = 15
        };

        _handler = new LogoutCommandHandler(
            _refreshTokenRepositoryMock.Object,
            _tokenBlacklistServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _jwtTokenServiceMock.Object,
            _sessionRepositoryMock.Object,
            _credentialRevocationMock.Object,
            new Mock<IIdpSessionRepository>().Object,
            _publisherMock.Object,
            TestHelpers.CreateOptions(_jwtSettings),
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_SingleDeviceLogoutWithNoRefreshTokenSent_StillRevokesTheSessionCredentials()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // The console signs out without sending the refresh token, which used to
        // mean the single-device branch revoked nothing: the session row was
        // ended and the refresh token bound to it stayed valid for its full week.
        var command = new LogoutCommand(userId, null, "access.jwt.token", "127.0.0.1", false)
        {
            SessionId = sessionId
        };

        await _handler.Handle(command, CancellationToken.None);

        _credentialRevocationMock.Verify(
            c => c.TerminateSessionAsync(sessionId, userId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());

        // Ending the row on its own is what the bug was; it must not be the
        // whole of what signing out does.
        _sessionRepositoryMock.Verify(
            s => s.TerminateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    private static LogoutCommand CreateCommand(
        Guid? userId = null,
        string? refreshToken = "refresh-token",
        string? accessToken = "access.jwt.token",
        string? ipAddress = "127.0.0.1",
        bool logoutAllDevices = false)
        => new(userId ?? Guid.NewGuid(), refreshToken, accessToken, ipAddress, logoutAllDevices);

    [Fact]
    public async Task Handle_SingleDeviceLogout_RevokesRefreshTokenAndBlacklistsAccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = CreateCommand(userId: userId);
        var storedToken = TestHelpers.CreateRefreshToken(userId: userId);

        _jwtTokenServiceMock
            .Setup(s => s.GetTokenId(command.AccessToken!))
            .Returns("jti-123");
        _jwtTokenServiceMock
            .Setup(s => s.GetTokenExpiry(command.AccessToken!))
            .Returns(DateTime.UtcNow.AddMinutes(15));
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken!))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken("jti-123", It.IsAny<DateTime>()),
            Times.Once());
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(storedToken, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_LogoutAllDevices_RevokesAllTokensAndBlacklistsAllUserTokens()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = CreateCommand(userId: userId, logoutAllDevices: true);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistAllUserTokens(userId, It.IsAny<DateTime>()),
            Times.Once());
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, userId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_Always_PublishesUserLoggedOutEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = CreateCommand(userId: userId);

        _jwtTokenServiceMock
            .Setup(s => s.GetTokenId(It.IsAny<string>()))
            .Returns("jti-123");
        _jwtTokenServiceMock
            .Setup(s => s.GetTokenExpiry(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddMinutes(15));
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns("hashed");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId));

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<UserLoggedOutEvent>(e => e.UserId == userId && e.AllDevices == false),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NoRefreshToken_StillSucceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new LogoutCommand(userId, null, "access.jwt.token", "127.0.0.1", false);

        _jwtTokenServiceMock
            .Setup(s => s.GetTokenId(It.IsAny<string>()))
            .Returns("jti-123");
        _jwtTokenServiceMock
            .Setup(s => s.GetTokenExpiry(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddMinutes(15));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_AlreadyRevokedRefreshToken_DoesNotRevokeAgain()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = CreateCommand(userId: userId);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: userId,
            reasonRevoked: "Already revoked");

        _jwtTokenServiceMock
            .Setup(s => s.GetTokenId(It.IsAny<string>()))
            .Returns("jti-123");
        _jwtTokenServiceMock
            .Setup(s => s.GetTokenExpiry(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddMinutes(15));
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken!))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }
}
