using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
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
    private readonly Mock<IPublisher> _publisherMock;
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
        _publisherMock = new Mock<IPublisher>();

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
            _publisherMock.Object,
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

    // Privilege-escalation regressions: an app-scoped refresh token whose
    // application is soft-deleted (repository returns null) or inactive must be
    // rejected outright. Falling back to the platform audience would upgrade an
    // app-scoped token into one the platform API itself accepts.

    [Fact]
    public async Task Handle_AppScopedToken_DeletedApplication_RejectsWithoutMintingToken()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            applicationId: applicationId,
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

        // Soft-deleted applications are invisible to GetByIdAsync.
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.Application?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.Inactive");

        _jwtTokenServiceMock.Verify(
            s => s.GenerateAccessToken(
                It.IsAny<User>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AppScopedToken_InactiveApplication_RejectsWithoutMintingToken()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var application = TestHelpers.CreateApplication(id: applicationId, code: "CRM", isActive: false);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            applicationId: applicationId,
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
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Application.Inactive");

        _jwtTokenServiceMock.Verify(
            s => s.GenerateAccessToken(
                It.IsAny<User>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AppScopedToken_ActiveApplication_MintsAppAudience()
    {
        // Arrange
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        var application = TestHelpers.CreateApplication(id: applicationId, code: "CRM", isActive: true);
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            applicationId: applicationId,
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
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                "CRM"))
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

        // Assert: the refreshed token keeps the app's own audience.
        result.IsError.Should().BeFalse();
        _jwtTokenServiceMock.Verify(
            s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                "CRM"),
            Times.Once);
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
        // "Rotated" is what makes a replay suspicious: the token was SPENT and
        // superseded, so a second presentation means two parties hold it.
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: Guid.NewGuid(),
            reasonRevoked: TokenRevocationReasons.Rotated);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        // Stated rather than left to the loose mock's default: the count decides
        // whether the account owner is emailed, so a test that silently rides
        // "0" would look like coverage of the notify path while never entering it.
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(
                userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

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
    public async Task Handle_RevokedToken_WhenLiveSessionsWereEnded_NotifiesTheAccountOwner()
    {
        // Arrange
        var command = CreateCommand(ipAddress: "31.223.57.26");
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId, email: "victim@test.com");
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: userId,
            reasonRevoked: "Rotated");

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(
                userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _publisherMock.Verify(
            p => p.Publish(
                It.Is<RefreshTokenReuseDetectedEvent>(e =>
                    e.UserId == userId &&
                    e.Email == "victim@test.com" &&
                    e.IpAddress == "31.223.57.26"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_RevokedToken_WhenNothingWasLeftToRevoke_SendsNoNotice()
    {
        // A rotated token replayed after everything was already revoked: the
        // detection is genuine, but nothing live was taken away, so there is
        // nothing to tell the owner that they have not already been told.
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: null,
            reasonRevoked: TokenRevocationReasons.Rotated);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(
                userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _userRepositoryMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Theory]
    [InlineData(TokenRevocationReasons.RefreshTokenReuse)]
    [InlineData("User initiated logout from all devices")]
    [InlineData("User account locked")]
    [InlineData("Account permanently deleted")]
    public async Task Handle_TokenKilledInBulk_EndsTheSessionWithoutRaisingAnAlarm(string reason)
    {
        // The device holding this token never spent it - a server-side mass
        // revocation killed it. Presenting it is the account owner's other
        // device finding out its session ended elsewhere, NOT evidence of theft.
        //
        // Treating it as a fresh attack is what made one incident
        // self-perpetuating: every innocent device triggered another mass
        // revocation, which killed whatever session the user had just signed
        // back in to. Signing in on one device knocked out the other, forever,
        // with an alarming e-mail each time.
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: null,
            reasonRevoked: reason);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert — a plain "this session is over", and nothing else happens.
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_RevokedTokenWithNoStatedReason_StillTreatedAsReuse(string? reason)
    {
        // The conservative default. An unknown reason must never be the thing
        // that makes detection fall silent.
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: null,
            reasonRevoked: reason);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(
                userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_RevokedToken_WhenTheAccountIsGone_StillRevokesAndDoesNotThrow()
    {
        // A hard-deleted account can still have a lingering revoked token pointed
        // at it. There is then no address to write to — and no reason to fail.
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: userId,
            reasonRevoked: "Rotated");

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(
                userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_RevokedToken_WhenTheNoticeFails_StillReturnsTokenRevoked()
    {
        // The revocation has already committed. Turning a clean 403 into a 500
        // because an email could not be raised would be strictly worse.
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var storedToken = TestHelpers.CreateRefreshToken(
            userId: userId,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: userId,
            reasonRevoked: "Rotated");

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedToken);
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(
                userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));
        _publisherMock
            .Setup(p => p.Publish(
                It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("notification pipeline down"));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
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
