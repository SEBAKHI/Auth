using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Queries;

public class IntrospectTokenQueryHandlerTests
{
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<ITokenBlacklistService> _tokenBlacklistServiceMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<ILogger<IntrospectTokenQueryHandler>> _loggerMock;
    private readonly IntrospectTokenQueryHandler _handler;

    public IntrospectTokenQueryHandlerTests()
    {
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _tokenBlacklistServiceMock = new Mock<ITokenBlacklistService>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _loggerMock = new Mock<ILogger<IntrospectTokenQueryHandler>>();

        _handler = new IntrospectTokenQueryHandler(
            _jwtTokenServiceMock.Object,
            _tokenBlacklistServiceMock.Object,
            _refreshTokenRepositoryMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_EmptyToken_ReturnsInactive()
    {
        // Arrange
        var query = new IntrospectTokenQuery("", null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidAccessToken_ReturnsActiveWithClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = new IntrospectTokenQuery("header.payload.signature", TokenTypeHint.AccessToken);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("jti", "token-jti"),
            new Claim("sub", userId.ToString()),
            new Claim("email", "test@example.com"),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds().ToString())
        }));

        _jwtTokenServiceMock
            .Setup(s => s.ValidateAccessToken(query.Token))
            .Returns(claims);
        _tokenBlacklistServiceMock
            .Setup(s => s.IsTokenBlacklisted("token-jti"))
            .Returns(false);
        _tokenBlacklistServiceMock
            .Setup(s => s.AreUserTokensBlacklisted(userId, It.IsAny<DateTime>()))
            .Returns(false);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeTrue();
        result.Value.Sub.Should().Be(userId.ToString());
        result.Value.Email.Should().Be("test@example.com");
        result.Value.Jti.Should().Be("token-jti");
    }

    [Fact]
    public async Task Handle_BlacklistedAccessToken_ReturnsInactive()
    {
        // Arrange
        var query = new IntrospectTokenQuery("header.payload.signature", TokenTypeHint.AccessToken);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("jti", "blacklisted-jti"),
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        }));

        _jwtTokenServiceMock
            .Setup(s => s.ValidateAccessToken(query.Token))
            .Returns(claims);
        _tokenBlacklistServiceMock
            .Setup(s => s.IsTokenBlacklisted("blacklisted-jti"))
            .Returns(true);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_InvalidAccessToken_ReturnsInactive()
    {
        // Arrange
        var query = new IntrospectTokenQuery("invalid.jwt.token", TokenTypeHint.AccessToken);

        _jwtTokenServiceMock
            .Setup(s => s.ValidateAccessToken(query.Token))
            .Returns(Error.Validation("Token.Invalid", "Invalid token"));

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidRefreshToken_ReturnsActive()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = new IntrospectTokenQuery("refresh-token", TokenTypeHint.RefreshToken);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            expiresAt: DateTime.UtcNow.AddDays(7));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(query.Token))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeTrue();
        result.Value.TokenType.Should().Be("refresh_token");
        result.Value.Sub.Should().Be(userId.ToString());
    }

    [Fact]
    public async Task Handle_ExpiredRefreshToken_ReturnsInactive()
    {
        // Arrange
        var query = new IntrospectTokenQuery("expired-token", TokenTypeHint.RefreshToken);
        var storedToken = TestHelpers.CreateRefreshToken(
            expiresAt: DateTime.UtcNow.AddDays(-1));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(query.Token))
            .Returns("hashed-expired");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-expired", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NonExistentRefreshToken_ReturnsInactive()
    {
        // Arrange
        var query = new IntrospectTokenQuery("unknown-token", TokenTypeHint.RefreshToken);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(query.Token))
            .Returns("hashed-unknown");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.Active.Should().BeFalse();
    }
}
