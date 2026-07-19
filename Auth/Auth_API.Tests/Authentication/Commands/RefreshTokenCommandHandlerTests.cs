using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

public class RefreshTokenCommandHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionRepository> _permissionRepositoryMock;
    private readonly Mock<IOrganizationRepository> _organizationRepositoryMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<ILogger<RefreshTokenCommandHandler>> _loggerMock;
    private readonly JwtSettings _jwtSettings;
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionRepositoryMock = new Mock<IPermissionRepository>();
        _organizationRepositoryMock = new Mock<IOrganizationRepository>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _loggerMock = new Mock<ILogger<RefreshTokenCommandHandler>>();

        _jwtSettings = new JwtSettings
        {
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7,
            RotateRefreshTokens = true
        };

        _handler = new RefreshTokenCommandHandler(
            _userRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _permissionRepositoryMock.Object,
            _organizationRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            new Mock<IUserSessionRepository>().Object,
            TestHelpers.CreateOptions(_jwtSettings),
            _loggerMock.Object);
    }

    private static RefreshTokenCommand CreateCommand(
        string refreshToken = "valid-refresh-token",
        string? ipAddress = "127.0.0.1",
        string? userAgent = "TestAgent/1.0")
        => new(refreshToken, ipAddress, userAgent);

    [Fact]
    public async Task Handle_ValidToken_ReturnsNewTokenResponse()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            expiresAt: DateTime.UtcNow.AddDays(7));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>());
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>()))
            .Returns("new-access-token");
        _jwtTokenServiceMock
            .Setup(s => s.GenerateRefreshToken())
            .Returns("new-refresh-token");
        _jwtTokenServiceMock
            .Setup(s => s.GetTokenId("new-access-token"))
            .Returns("new-jti");
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash("new-refresh-token"))
            .Returns("new-hashed-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.AccessToken.Should().Be("new-access-token");
        result.Value.RefreshToken.Should().Be("new-refresh-token");
    }

    [Fact]
    public async Task Handle_TokenNotFound_ReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshTokenEntity?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenNotFound.Code);
    }

    [Fact]
    public async Task Handle_RevokedToken_RevokesAllUserTokensAndReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: Guid.NewGuid(),
            reasonRevoked: "Test revoke");

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ExpiredToken_ReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        var storedToken = TestHelpers.CreateRefreshToken(
            expiresAt: DateTime.UtcNow.AddDays(-1));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenExpired.Code);
    }

    [Fact]
    public async Task Handle_UserNotFound_ReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            expiresAt: DateTime.UtcNow.AddDays(7));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_LockedUser_RevokesAllTokensAndReturnsError()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(
            id: userId,
            status: Auth.Domain.Enums.UserStatus.Locked);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            expiresAt: DateTime.UtcNow.AddDays(7));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_WithRotation_RevokesOldTokenAndCreatesNew()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            expiresAt: DateTime.UtcNow.AddDays(7));

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _roleRepositoryMock
            .Setup(r => r.GetUserRolesAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>());
        _permissionRepositoryMock
            .Setup(r => r.GetUserEffectivePermissionsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>()))
            .Returns("new-access-token");
        _jwtTokenServiceMock
            .Setup(s => s.GenerateRefreshToken())
            .Returns("new-refresh-token");
        _jwtTokenServiceMock
            .Setup(s => s.GetTokenId("new-access-token"))
            .Returns("new-jti");
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash("new-refresh-token"))
            .Returns("new-hashed-token");

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(storedToken, It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
