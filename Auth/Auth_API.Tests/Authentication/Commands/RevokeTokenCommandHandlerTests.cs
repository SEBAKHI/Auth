using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

public class RevokeTokenCommandHandlerTests
{
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<ITokenBlacklistService> _tokenBlacklistServiceMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<ILogger<RevokeTokenCommandHandler>> _loggerMock;
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _tokenBlacklistServiceMock = new Mock<ITokenBlacklistService>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _loggerMock = new Mock<ILogger<RevokeTokenCommandHandler>>();

        _handler = new RevokeTokenCommandHandler(
            _jwtTokenServiceMock.Object,
            _tokenBlacklistServiceMock.Object,
            _refreshTokenRepositoryMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyToken_ReturnsInvalidTokenError()
    {
        // Arrange
        var command = new RevokeTokenCommand("", null, null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidToken.Code);
    }

    [Fact]
    public async Task Handle_ValidAccessToken_BlacklistsToken()
    {
        // Arrange
        var command = new RevokeTokenCommand("header.payload.signature", TokenTypeHint.AccessToken, null);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("jti", "token-jti"),
            new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds().ToString())
        }));

        _jwtTokenServiceMock
            .Setup(s => s.ValidateAccessToken(command.Token))
            .Returns(claims);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken("token-jti", It.IsAny<DateTime>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ValidRefreshToken_RevokesToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var revokedBy = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(userId: userId);
        var command = new RevokeTokenCommand("refresh-token-value", TokenTypeHint.RefreshToken, revokedBy);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.Token))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(storedToken, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_NonExistentRefreshToken_ReturnsSuccessPerRfc7009()
    {
        // Arrange
        var command = new RevokeTokenCommand("unknown-token", TokenTypeHint.RefreshToken, null);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.Token))
            .Returns("hashed-unknown");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoTypeHint_JwtTokenDetectedAsAccessToken()
    {
        // Arrange — token with dots = JWT = access token
        var command = new RevokeTokenCommand("header.payload.signature", null, null);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("jti", "auto-detected-jti"),
            new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds().ToString())
        }));

        _jwtTokenServiceMock
            .Setup(s => s.ValidateAccessToken(command.Token))
            .Returns(claims);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken("auto-detected-jti", It.IsAny<DateTime>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_AlreadyRevokedRefreshToken_ReturnsSuccess()
    {
        // Arrange
        var storedToken = TestHelpers.CreateRefreshToken(
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: Guid.NewGuid(),
            reasonRevoked: "Already revoked");
        var command = new RevokeTokenCommand("revoked-token", TokenTypeHint.RefreshToken, null);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.Token))
            .Returns("hashed-revoked");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-revoked", It.IsAny<CancellationToken>()))
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
