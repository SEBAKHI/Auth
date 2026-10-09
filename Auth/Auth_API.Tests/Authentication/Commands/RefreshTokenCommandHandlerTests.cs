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
    private readonly Mock<ITokenClaimsResolver> _tokenClaimsResolverMock;
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock;
    private readonly Mock<IApplicationAccessRepository> _applicationAccessRepositoryMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock;
    private readonly Mock<ILogger<RefreshTokenCommandHandler>> _loggerMock;
    private readonly Mock<IPublisher> _publisherMock;
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly Mock<IUserSessionRepository> _sessionRepositoryMock = new();
    private readonly JwtSettings _jwtSettings;
    private readonly RefreshTokenCommandHandler _handler;

    public RefreshTokenCommandHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _tokenClaimsResolverMock = new Mock<ITokenClaimsResolver>();
        _applicationRepositoryMock = new Mock<IApplicationRepository>();
        _applicationAccessRepositoryMock = new Mock<IApplicationAccessRepository>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _refreshTokenKeyServiceMock = new Mock<IRefreshTokenKeyService>();
        _loggerMock = new Mock<ILogger<RefreshTokenCommandHandler>>();
        _publisherMock = new Mock<IPublisher>();

        _tokenClaimsResolverMock
            .Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));

        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // A rotation wins unless a test says it lost the race.
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // A session's refresh family is alive (it holds the rotated token's
        // successor) unless a test says the family is dead.
        _refreshTokenRepositoryMock
            .Setup(r => r.HasLiveTokenInSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _jwtSettings = new JwtSettings
        {
            AccessTokenLifetimeMinutes = 15,
            RefreshTokenLifetimeDays = 7,
            RotateRefreshTokens = true
        };

        _handler = new RefreshTokenCommandHandler(
            _userRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _tokenClaimsResolverMock.Object,
            TestHelpers.CreatePlatformMfaPolicy(),
            _applicationRepositoryMock.Object,
            _applicationAccessRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _sessionRepositoryMock.Object,
            _credentialRevocationMock.Object,
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
        _tokenClaimsResolverMock
            .Setup(r => r.ResolveAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
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
        _tokenClaimsResolverMock
            .Setup(r => r.ResolveAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));

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
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()),
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
        _tokenClaimsResolverMock
            .Setup(r => r.ResolveAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
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
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()),
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
        _tokenClaimsResolverMock
            .Setup(r => r.ResolveAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                "CRM",
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
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
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                "CRM",
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()),
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

    // --- Reuse goes through the credential-revocation service (OI-65 D5) ---

    /// <summary>A rotated token presented again: the reuse path, past the grace window.</summary>
    private RefreshTokenCommand SetupReusedToken(Guid userId, int liveRefreshTokens, Guid? sessionId = null)
    {
        var command = CreateCommand();
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(command.RefreshToken))
            .Returns("hashed-token");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("hashed-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(
                userId: userId,
                sessionId: sessionId,
                revokedAt: DateTime.UtcNow.AddMinutes(-5),
                revokedBy: userId,
                reasonRevoked: TokenRevocationReasons.Rotated));
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(liveRefreshTokens);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));
        return command;
    }

    [Fact]
    public async Task Handle_ReusedToken_EndsEverySessionAccessTokenAndSsoSession_AfterCountingTheRefreshTokens()
    {
        // Revoking the refresh tokens alone left the access tokens already out
        // working until they expired, and the SSO cookie able to mint new ones.
        var userId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 2);
        var order = new List<string>();
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("refresh tokens counted"))
            .ReturnsAsync(2);
        _credentialRevocationMock
            .Setup(c => c.RevokeAllCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("credentials wiped"))
            .ReturnsAsync(3);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
        // Counted first: the wipe sweeps the same rows, and a count taken after
        // it would always be zero, so the owner would never be told.
        order.Should().Equal("refresh tokens counted", "credentials wiped");
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(0, 5, 0)]
    public async Task Handle_ReusedToken_TheNoticeFollowsTheRefreshTokenCount_NotTheSessionCount(
        int liveRefreshTokens, int sessionsWiped, int notices)
    {
        // One incident produces many detections; only the first finds live refresh
        // tokens. The service's own count (sessions) must not re-open the gate.
        var userId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens);
        _credentialRevocationMock
            .Setup(c => c.RevokeAllCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionsWiped);

        await _handler.Handle(command, CancellationToken.None);

        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(notices));
    }

    [Fact]
    public async Task Handle_ReusedToken_WhenTheCredentialWipeFails_StillReturnsTokenRevoked_AndLogsOneError()
    {
        // The refresh tokens are already revoked, so neither holder can renew. A
        // 500 here would change nothing for the thief and mislead the client.
        var userId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 1);
        _credentialRevocationMock
            .Setup(c => c.RevokeAllCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
        // The owner is still told: their refresh tokens were taken away.
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ReusedToken_ReturnsBeforeThePlatformMfaPolicyAndTheMint()
    {
        // S08 reads the session row and evaluates the platform MFA policy before
        // the mint. A reused token never reaches that part: the reuse branch
        // answers first, so the wipe depends on neither. It reads no session row
        // at all; its family check asks the refresh tokens (F11), which hold a
        // live one here, so it cascades.
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 1, sessionId: sessionId);
        var policy = new Mock<IPlatformMfaPolicy>();
        var sessions = new Mock<IUserSessionRepository>();
        var handler = new RefreshTokenCommandHandler(
            _userRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _tokenClaimsResolverMock.Object,
            policy.Object,
            _applicationRepositoryMock.Object,
            _applicationAccessRepositoryMock.Object,
            _jwtTokenServiceMock.Object,
            _refreshTokenKeyServiceMock.Object,
            sessions.Object,
            _credentialRevocationMock.Object,
            _publisherMock.Object,
            TestHelpers.CreateOptions(_jwtSettings),
            _loggerMock.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
        policy.VerifyNoOtherCalls();
        sessions.VerifyNoOtherCalls();
        _refreshTokenRepositoryMock.Verify(
            r => r.HasLiveTokenInSessionAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once());
        _tokenClaimsResolverMock.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _jwtTokenServiceMock.Verify(
            j => j.GenerateAccessToken(
                It.IsAny<User>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<TokenOrganization?>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_ReusedToken_WhenTheCallerHasGoneAway_StillWipesAndNotifies()
    {
        // Once the refresh tokens are counted, the incident's one wipe and one
        // notice must not depend on the caller staying connected: a thief would
        // simply hang up.
        var userId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await _handler.Handle(command, cancellation.Token);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        // The count is the first write: committed and then abandoned, it would
        // spend the incident's notice with nothing else done.
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, null, TokenRevocationReasons.RefreshTokenReuse, CancellationToken.None),
            Times.Once());
        _credentialRevocationMock.Verify(
            c => c.RevokeAllCredentialsAsync(
                userId, null, TokenRevocationReasons.RefreshTokenReuse, CancellationToken.None),
            Times.Once());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), CancellationToken.None),
            Times.Once());
    }

    // --- F1/F11: the family rule. A handled incident does not fire again, and
    // the family's life is read from its refresh tokens, never from its row ---

    [Fact]
    public async Task Handle_RotatedTokenWhoseRowTheSweepEnded_ButWhoseFamilyIsAlive_CascadesAndNotifies()
    {
        // THE regression F11 pins. A user signed in more than a refresh lifetime
        // ago and still active: the row's expiry was fixed at sign-in, so the
        // daily sweep has ended it ('timeout'), while the refresh chain slides on.
        // A thief who used the stolen token first now holds the live one. The
        // genuine client's rotated token is theft evidence, row or no row.
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 1, sessionId: sessionId);
        _sessionRepositoryMock
            .Setup(s => s.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(id: sessionId, userId: userId, isActive: false));
        _refreshTokenRepositoryMock
            .Setup(r => r.HasLiveTokenInSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_SameRotatedTokenAgain_AfterTheCascadeKilledItsFamily_IsInert()
    {
        // The first detection cascades and revokes every token of the user. The
        // same stolen token pressed again must not sign the user out once more.
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 2, sessionId: sessionId);
        _refreshTokenRepositoryMock
            .SetupSequence(r => r.HasLiveTokenInSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var first = await _handler.Handle(command, CancellationToken.None);
        var second = await _handler.Handle(command, CancellationToken.None);

        first.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        second.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        // Exactly one cascade and one notice in total: the first detection's.
        VerifyBulkRevocation(userId);
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_SessionlessRotatedToken_StillCascades_WithoutAskingForItsFamily()
    {
        // No session to ask about: the conservative answer is the cascade, as before.
        var userId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 1, sessionId: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
        _refreshTokenRepositoryMock.Verify(
            r => r.HasLiveTokenInSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_RotatedTokenWhoseFamilyCannotBeRead_StillCascades()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var command = SetupReusedToken(userId, liveRefreshTokens: 1, sessionId: sessionId);
        _refreshTokenRepositoryMock
            .Setup(r => r.HasLiveTokenInSessionAsync(sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
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
    public async Task Handle_DeactivatedUser_RefusesAndRevokesEveryRemainingToken()
    {
        // The offboarding hole, at the door it was walked through. This path used
        // to ask IsLockedOut(), which only matches Locked, so a deactivated
        // account renewed normally — and because rotation restarts the refresh
        // window on every use, it renewed indefinitely.
        var command = CreateCommand();
        var userId = Guid.NewGuid();
        var user = TestHelpers.CreateUser(
            id: userId,
            status: Auth.Domain.Enums.UserStatus.Inactive);
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

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();

        // Refusing this one request is not enough: whatever the token was, it is
        // still in someone's browser, so the refusal also has to take the rest.
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
        _tokenClaimsResolverMock
            .Setup(r => r.ResolveAsync(userId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
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

        // Assert: one atomic write revokes the old token and creates the new one.
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(storedToken, It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    // ------------------------------------------------------------------
    // S01: atomic rotation, the loser of a race, and the replay grace window.
    // ------------------------------------------------------------------

    private (Auth.Domain.Entities.User User, RefreshTokenEntity Presented) ArrangeRefresh(
        string presentedToken,
        RefreshTokenEntity? presented = null)
    {
        var userId = presented?.UserId ?? Guid.NewGuid();
        var user = TestHelpers.CreateUser(id: userId);
        presented ??= TestHelpers.CreateRefreshToken(
            userId: userId, expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid());

        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash(presentedToken)).Returns("presented-hash");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("presented-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(presented);
        _userRepositoryMock.Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _jwtTokenServiceMock
            .Setup(s => s.GenerateAccessToken(
                user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("new-access-token");
        _jwtTokenServiceMock.Setup(s => s.GenerateRefreshToken()).Returns("new-refresh-token");
        _jwtTokenServiceMock.Setup(s => s.GetTokenId("new-access-token")).Returns("new-jti");
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("new-refresh-token")).Returns("new-hash");
        _refreshTokenRepositoryMock
            .Setup(r => r.RevokeAllForUserAsync(userId, null, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return (user, presented);
    }

    private static RefreshTokenEntity RotatedToken(Guid userId, TimeSpan ago, string? replacedBy) =>
        TestHelpers.CreateRefreshToken(
            userId: userId,
            expiresAt: DateTime.UtcNow.AddDays(7),
            revokedAt: DateTime.UtcNow - ago,
            revokedBy: userId,
            reasonRevoked: TokenRevocationReasons.Rotated,
            replacedByTokenHash: replacedBy,
            sessionId: Guid.NewGuid());

    private void VerifyNoBulkRevocationAndNoMail()
    {
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _credentialRevocationMock.Verify(
            c => c.RevokeAllCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    private void VerifyBulkRevocation(Guid userId)
    {
        _refreshTokenRepositoryMock.Verify(
            r => r.RevokeAllForUserAsync(userId, null, TokenRevocationReasons.RefreshTokenReuse, It.IsAny<CancellationToken>()),
            Times.Once());
        _credentialRevocationMock.Verify(
            c => c.RevokeAllCredentialsAsync(userId, null, TokenRevocationReasons.RefreshTokenReuse, It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ValidToken_RotatesInOneAtomicWrite_NamingTheReplacement()
    {
        var (_, presented) = ArrangeRefresh("t0");

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(
                It.Is<RefreshTokenEntity>(t => t.Id == presented.Id
                    && t.ReasonRevoked == TokenRevocationReasons.Rotated
                    && t.ReplacedByTokenHash == "new-hash"),
                It.Is<RefreshTokenEntity>(t => t.TokenHash == "new-hash" && t.SessionId == presented.SessionId),
                It.IsAny<CancellationToken>()),
            Times.Once());
        // The old non-atomic pair is gone: no separate create, no unconditional update.
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData(false)] // (1) body channel
    [InlineData(true)]  // (2) cookie channel
    public async Task Handle_LosesTheRotationRace_SucceedsWithASiblingToken_AndRevokesNothing(bool fromCookie)
    {
        var (_, presented) = ArrangeRefresh("t0");
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // What the winner left behind: the same token, rotated a moment ago, and
        // the winner's replacement, still live.
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(presented.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(presented.UserId, TimeSpan.FromMilliseconds(5), "winner-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("winner-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: presented.UserId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = fromCookie }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RefreshToken.Should().Be("new-refresh-token");
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<RefreshTokenEntity>(t => t.TokenHash == "new-hash" && t.SessionId == presented.SessionId),
                It.IsAny<CancellationToken>()),
            Times.Once());
        VerifyNoBulkRevocationAndNoMail();
    }

    [Fact]
    public async Task Handle_LosesTheRotationRaceToASessionEnd_IsRefused_AndMintsNoSibling()
    {
        // The token was live when read, and a sign-out ended its session before
        // the rotation landed. A sibling here would outlive that sign-out.
        var (_, presented) = ArrangeRefresh("t0");
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(presented.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(
                id: presented.Id, userId: presented.UserId,
                revokedAt: DateTime.UtcNow, reasonRevoked: "User logout"));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
        VerifyNoBulkRevocationAndNoMail();
    }

    [Fact]
    public async Task Handle_LateCookiePresentationWithinGrace_AnswersOnce_RevokingTheReplacementWithoutASuccessor()
    {
        // (3) T0 was rotated to T1 five seconds ago and the response was lost.
        var userId = Guid.NewGuid();
        var t0 = RotatedToken(userId, TimeSpan.FromSeconds(5), "t1-hash");
        ArrangeRefresh("t0", t0);
        var t1 = TestHelpers.CreateRefreshToken(
            userId: userId, tokenHash: "t1-hash", expiresAt: DateTime.UtcNow.AddDays(7), sessionId: t0.SessionId);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(t1);

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RefreshToken.Should().Be("new-refresh-token");
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(
                It.Is<RefreshTokenEntity>(t => t.Id == t1.Id
                    && t.ReasonRevoked == TokenRevocationReasons.Rotated
                    && t.ReplacedByTokenHash == null),
                It.Is<RefreshTokenEntity>(t => t.TokenHash == "new-hash" && t.SessionId == t1.SessionId),
                It.IsAny<CancellationToken>()),
            Times.Once());
        VerifyNoBulkRevocationAndNoMail();
    }

    [Fact]
    public async Task Handle_SecondHolderPresentsTheGraceRevokedReplacement_TriggersReuseDetection()
    {
        // (4) T1 was revoked by a grace answer: rotated, with no successor.
        var userId = Guid.NewGuid();
        ArrangeRefresh("t1", RotatedToken(userId, TimeSpan.FromSeconds(2), replacedBy: null));

        var result = await _handler.Handle(
            CreateCommand("t1") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
    }

    [Fact]
    public async Task Handle_SecondPresentationOfTheSameRotatedToken_TriggersReuseDetection()
    {
        // (5) T0 within grace, but its replacement T1 is already spent.
        var userId = Guid.NewGuid();
        ArrangeRefresh("t0", RotatedToken(userId, TimeSpan.FromSeconds(5), "t1-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(userId, TimeSpan.FromSeconds(1), replacedBy: null));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_GraceAnswerLosesTheRaceForTheReplacement_TriggersReuseDetection()
    {
        var userId = Guid.NewGuid();
        var t0 = RotatedToken(userId, TimeSpan.FromSeconds(5), "t1-hash");
        ArrangeRefresh("t0", t0);
        var t1 = TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(t1);
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Another holder spent T1 first, and its chain is live: two parties hold it.
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(t1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(userId, TimeSpan.FromMilliseconds(5), "t2-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t2-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_GraceAnswerLosesToASignOutInFlight_EndsTheSession_WithoutACascade()
    {
        // The replacement T1 was ended by a sign-out while this grace answer was
        // in flight: the session is over, which is not evidence of theft.
        var userId = Guid.NewGuid();
        ArrangeRefresh("t0", RotatedToken(userId, TimeSpan.FromSeconds(5), "t1-hash"));
        var t1 = TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(t1);
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(t1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(
                id: t1.Id, userId: userId, revokedAt: DateTime.UtcNow, reasonRevoked: "User logout"));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        VerifyNoBulkRevocationAndNoMail();
    }

    [Fact]
    public async Task Handle_LosesTheRaceAndTheWinnersReplacementWasEndedInFlight_IsRefused_AndMintsNoSibling()
    {
        // The winner rotated T to R, then a sign-out revoked R (T was no longer
        // live, so the sign-out never touched it). A sibling here would outlive
        // that sign-out.
        var (_, presented) = ArrangeRefresh("t0");
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(presented.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(presented.UserId, TimeSpan.FromMilliseconds(5), "winner-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("winner-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(
                userId: presented.UserId, revokedAt: DateTime.UtcNow, reasonRevoked: "User logout"));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
        VerifyNoBulkRevocationAndNoMail();
    }

    [Fact]
    public async Task Handle_AnApplicationTokenClaimedFromTheCookie_GetsNoGrace()
    {
        // The channel is the request's claim; a non-browser client can forge it.
        // First-party sessions carry no application, so an application's token
        // presented "from the cookie" within the window is reuse, as ever.
        var userId = Guid.NewGuid();
        var t0 = TestHelpers.CreateRefreshToken(
            userId: userId, applicationId: Guid.NewGuid(), expiresAt: DateTime.UtcNow.AddDays(7),
            revokedAt: DateTime.UtcNow.AddSeconds(-5), reasonRevoked: TokenRevocationReasons.Rotated,
            replacedByTokenHash: "t1-hash");
        ArrangeRefresh("t0", t0);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
    }

    [Fact]
    public async Task Handle_CookiePresentationAfterTheGraceWindow_TriggersReuseDetection()
    {
        // (6) Rotated 31 s ago with a 30 s window.
        var userId = Guid.NewGuid();
        ArrangeRefresh("t0", RotatedToken(userId, TimeSpan.FromSeconds(31), "t1-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
    }

    [Fact]
    public async Task Handle_LateBodyPresentationWithinGrace_TriggersReuseDetectionAsToday()
    {
        // (7) A body token is script-readable: no grace, whatever the timing.
        var userId = Guid.NewGuid();
        ArrangeRefresh("t0", RotatedToken(userId, TimeSpan.FromSeconds(5), "t1-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
    }

    [Fact]
    public async Task Handle_ReplacementCannotBeCreated_FailsTransiently_WithoutBulkRevocation()
    {
        // (8) The repository rolls the revocation back with the failed insert
        // (RefreshTokenRepositorySqlGuardTests); here the failure must surface as
        // a transient fault, never as reuse.
        ArrangeRefresh("t0");
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("insert failed"));

        var act = () => _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        VerifyNoBulkRevocationAndNoMail();
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_RotationDisabled_ReturnsTheSameToken_ForItsRemainingLifetime_WithoutRotating()
    {
        // (9)
        _jwtSettings.RotateRefreshTokens = false;
        var userId = Guid.NewGuid();
        var presented = TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddHours(2));
        ArrangeRefresh("t0", presented);

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RefreshToken.Should().Be("t0");
        result.Value.RefreshExpiresIn.Should().BeInRange(7190, 7200);
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_TokenEndedInBulk_FromTheCookie_StillAnswersSessionEnded_WithoutCascade()
    {
        // (10) The WasTerminatedInBulk branch is untouched by the grace window.
        var userId = Guid.NewGuid();
        ArrangeRefresh("t0", TestHelpers.CreateRefreshToken(
            userId: userId, revokedAt: DateTime.UtcNow.AddSeconds(-2),
            reasonRevoked: TokenRevocationReasons.RefreshTokenReuse, replacedByTokenHash: "t1-hash"));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        VerifyNoBulkRevocationAndNoMail();
    }

    // ------------------------------------------------------------------
    // OI-58: the granted scope is narrowed by a refresh, never widened.
    // ------------------------------------------------------------------

    /// <summary>
    /// An application refresh token carrying <paramref name="storedScope"/>, for an
    /// application that allows <paramref name="allowedNow"/> at this refresh.
    /// </summary>
    private RefreshTokenEntity ArrangeApplicationRefresh(string? storedScope, string? allowedNow)
    {
        var application = TestHelpers.CreateApplication(code: "EDIS", isActive: true);
        application.LoadAllowedScopes(allowedNow);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var presented = TestHelpers.CreateRefreshToken(
            applicationId: application.Id,
            expiresAt: DateTime.UtcNow.AddDays(7),
            sessionId: Guid.NewGuid(),
            scope: storedScope);
        ArrangeRefresh("t0", presented);
        return presented;
    }

    /// <summary>The scope argument the access token was minted with.</summary>
    private void VerifyAccessTokenScope(string? scope) =>
        _jwtTokenServiceMock.Verify(
            s => s.GenerateAccessToken(
                It.IsAny<User>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                scope, It.IsAny<TokenOrganization?>()),
            Times.Once());

    private void VerifyRotatedTokenScope(string? scope) =>
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(
                It.IsAny<RefreshTokenEntity>(),
                It.Is<RefreshTokenEntity>(t => t.Scope == scope),
                It.IsAny<CancellationToken>()),
            Times.Once());

    [Fact]
    public async Task Handle_ApplicationToken_NarrowsTheStoredGrantToWhatIsAllowedNow()
    {
        // An administrator removed email and phone since the sign-in.
        ArrangeApplicationRefresh(storedScope: "openid profile email phone", allowedNow: "profile");

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Scope.Should().Be("openid profile");
        VerifyAccessTokenScope("openid profile");
        VerifyRotatedTokenScope("openid profile");
    }

    [Fact]
    public async Task Handle_ApplicationTokenWithoutStoredGrant_IsOpenIdOnly()
    {
        // A token minted by the previous build has no Scope.
        ArrangeApplicationRefresh(storedScope: null, allowedNow: "profile email phone");

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.Value.Scope.Should().Be("openid");
        VerifyAccessTokenScope("openid");
        VerifyRotatedTokenScope("openid");
    }

    [Fact]
    public async Task Handle_ApplicationToken_NeverWidensTheStoredGrant()
    {
        // The application now allows everything; the session was granted openid.
        // Widening takes a new authorize, never a refresh.
        ArrangeApplicationRefresh(storedScope: "openid", allowedNow: "profile email phone");

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.Value.Scope.Should().Be("openid");
        VerifyAccessTokenScope("openid");
        VerifyRotatedTokenScope("openid");
    }

    [Fact]
    public async Task Handle_PlatformToken_CarriesAndReturnsNoScope()
    {
        ArrangeRefresh("t0");

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Scope.Should().BeNull();
        VerifyAccessTokenScope(null);
        VerifyRotatedTokenScope(null);
    }

    [Fact]
    public async Task Handle_LostRotationRace_TheSiblingCarriesTheNarrowedGrant()
    {
        var presented = ArrangeApplicationRefresh(storedScope: "openid email phone", allowedNow: "email");
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(presented.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(presented.UserId, TimeSpan.FromMilliseconds(5), "winner-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("winner-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: presented.UserId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.Value.Scope.Should().Be("openid email");
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(
                It.Is<RefreshTokenEntity>(t => t.TokenHash == "new-hash" && t.Scope == "openid email"),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_RotationOff_LeavesTheStoredRowAndMintsTheNarrowedGrant()
    {
        _jwtSettings.RotateRefreshTokens = false;
        var presented = ArrangeApplicationRefresh(storedScope: "openid profile phone", allowedNow: "phone");

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.Value.Scope.Should().Be("openid phone");
        VerifyAccessTokenScope("openid phone");
        presented.Scope.Should().Be("openid profile phone", "with rotation off nothing rewrites the stored row");
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // ------------------------------------------------------------------
    // OI-101: an application's refresh token gets a short replay grace, and
    // only when the request names that application with client_id (RFC 6749 §6).
    // ------------------------------------------------------------------

    private const string EdisClientId = "EDIS";

    /// <summary>
    /// EDIS's token t0 was rotated to t1 <paramref name="ago"/> ago and the response
    /// was lost; t1 is live. EDIS is active and resolves from its client_id.
    /// </summary>
    private (Guid ApplicationId, RefreshTokenEntity T0, RefreshTokenEntity T1) ArrangeApplicationReplay(TimeSpan ago)
    {
        var application = TestHelpers.CreateApplication(code: EdisClientId, isActive: true);
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(EdisClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _applicationRepositoryMock
            .Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var t0 = TestHelpers.CreateRefreshToken(
            userId: userId, applicationId: application.Id, expiresAt: DateTime.UtcNow.AddDays(7),
            revokedAt: DateTime.UtcNow - ago, revokedBy: userId, reasonRevoked: TokenRevocationReasons.Rotated,
            replacedByTokenHash: "t1-hash", sessionId: sessionId);
        ArrangeRefresh("t0", t0);

        var t1 = TestHelpers.CreateRefreshToken(
            userId: userId, applicationId: application.Id, tokenHash: "t1-hash",
            expiresAt: DateTime.UtcNow.AddDays(7), sessionId: sessionId);
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("t1")).Returns("t1-hash");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(t1);

        return (application.Id, t0, t1);
    }

    private static RefreshTokenCommand ApplicationCommand(string token, string? clientId = EdisClientId) =>
        CreateCommand(token) with { ClientId = clientId };

    /// <summary>
    /// A rotation's replacement becomes findable by its hash, so a later
    /// presentation in the same test sees what the handler wrote. The rotated token
    /// itself is the very instance the repository handed out, revoked in place.
    /// </summary>
    private void RememberReplacements() =>
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity _, RefreshTokenEntity replacement, CancellationToken _) =>
                _refreshTokenRepositoryMock
                    .Setup(r => r.GetByTokenHashAsync(replacement.TokenHash, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(replacement))
            .ReturnsAsync(true);

    private void VerifyWarningLogged(string text, Times times) =>
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(text)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private void VerifyReuseAlert(Guid userId) =>
        _publisherMock.Verify(
            p => p.Publish(It.Is<RefreshTokenReuseDetectedEvent>(e => e.UserId == userId), It.IsAny<CancellationToken>()),
            Times.Once());

    /// <summary>Nothing revoked, rotated, minted, written or counted as reuse.</summary>
    private void VerifyTheTokenWasNeverTouched()
    {
        VerifyNoBulkRevocationAndNoMail();
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
        _refreshTokenRepositoryMock.Verify(
            r => r.HasLiveTokenInSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _sessionRepositoryMock.Verify(
            r => r.TouchOnRefreshAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _jwtTokenServiceMock.Verify(
            s => s.GenerateAccessToken(
                It.IsAny<User>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_ApplicationTokenAgainWithinItsWindow_FromThatApplication_AnswersOnce_RevokingTheReplacementWithoutASuccessor()
    {
        // T1: EDIS lost a refresh response 5 s ago and retries with t0, naming itself.
        var (applicationId, _, t1) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));

        var result = await _handler.Handle(ApplicationCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RefreshToken.Should().Be("new-refresh-token");
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(
                It.Is<RefreshTokenEntity>(t => t.Id == t1.Id
                    && t.ReasonRevoked == TokenRevocationReasons.Rotated
                    && t.ReplacedByTokenHash == null),
                It.Is<RefreshTokenEntity>(t => t.TokenHash == "new-hash"
                    && t.SessionId == t1.SessionId
                    && t.ApplicationId == applicationId),
                It.IsAny<CancellationToken>()),
            Times.Once());
        VerifyNoBulkRevocationAndNoMail();
        VerifyWarningLogged("RefreshToken.ApplicationReplayGraceUsed", Times.Once());
        VerifyWarningLogged("RefreshToken.ReplayGraceUsed:", Times.Never());
    }

    [Theory]
    [InlineData("t0")] // the token whose response was lost, presented a third time
    [InlineData("t1")] // the replacement the grace answer spent
    public async Task Handle_AfterTheApplicationGraceAnswer_ASpentTokenPresentedAgain_CascadesWithTheAlert(string replayed)
    {
        // T2: the grace is single-use. Whoever presents a spent token next is reuse.
        var (_, t0, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
        RememberReplacements();
        (await _handler.Handle(ApplicationCommand("t0"), CancellationToken.None)).IsError.Should().BeFalse();

        var result = await _handler.Handle(ApplicationCommand(replayed), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(t0.UserId);
        VerifyReuseAlert(t0.UserId);
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Once(), "only the grace answer rotated anything");
    }

    [Fact]
    public async Task Handle_ApplicationGraceAnswerLosesTheRaceForTheReplacement_Cascades()
    {
        // Two retries of the same lost refresh in flight at once: one is answered
        // from the grace, the other finds the replacement already spent. The grace
        // answers one retry, never two, which is why the integration guide asks for
        // one refresh in flight at a time.
        var (_, t0, t1) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(t1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(t0.UserId, TimeSpan.FromMilliseconds(5), "t2-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t2-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: t0.UserId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(ApplicationCommand("t0"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(t0.UserId);
        VerifyReuseAlert(t0.UserId);
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData(true)]  // an application's token, its client_id named
    [InlineData(false)] // a first-party token, from the cookie
    public async Task Handle_NormalRotationLosesToAGraceAnswer_CascadesWithTheAlert(bool applicationToken)
    {
        // F5 (REVIEW 56): this request presents the live t1 while a retry of t0 is
        // answered from the grace a moment earlier, spending t1 with no successor.
        // Two parties hold the chain: the cascade runs, and no sibling is minted.
        RefreshTokenEntity t1;
        RefreshTokenCommand command;
        if (applicationToken)
        {
            (_, _, t1) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
            command = ApplicationCommand("t1");
        }
        else
        {
            var (_, presented) = ArrangeRefresh("t1");
            t1 = presented;
            command = CreateCommand("t1") with { ReplayGraceEligible = true };
        }

        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(t1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(
                id: t1.Id, userId: t1.UserId, applicationId: t1.ApplicationId, expiresAt: t1.ExpiresAt,
                revokedAt: DateTime.UtcNow, revokedBy: t1.UserId, reasonRevoked: TokenRevocationReasons.Rotated,
                replacedByTokenHash: null, sessionId: t1.SessionId));

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(t1.UserId);
        VerifyReuseAlert(t1.UserId);
        _refreshTokenRepositoryMock.Verify(
            r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_ApplicationTokenAgainWithinTheWindow_WithoutClientId_CascadesAsToday()
    {
        // T3: an integration that sends no client_id keeps today's behaviour.
        var (_, t0, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));

        var result = await _handler.Handle(ApplicationCommand("t0", clientId: null), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(t0.UserId);
        VerifyReuseAlert(t0.UserId);
        _applicationRepositoryMock.Verify(
            r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>The client_id for one way of naming a client the token was not issued to.</summary>
    private string ArrangeClientMismatch(string mismatch, Guid applicationId)
    {
        switch (mismatch)
        {
            case "another application":
                _applicationRepositoryMock
                    .Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateApplication(code: "CRM", isActive: true));
                return "CRM";
            case "unknown":
                return "NO-SUCH-APP";
            case "inactive":
                _applicationRepositoryMock
                    .Setup(r => r.GetByCodeAsync(EdisClientId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateApplication(id: applicationId, code: EdisClientId, isActive: false));
                return EdisClientId;
            case "first-party token":
                // A platform session's token, rotated 5 s ago, sent to the token
                // endpoint with an application's client_id.
                var userId = Guid.NewGuid();
                ArrangeRefresh("t0", RotatedToken(userId, TimeSpan.FromSeconds(5), "fp-t1-hash"));
                _refreshTokenRepositoryMock
                    .Setup(r => r.GetByTokenHashAsync("fp-t1-hash", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7)));
                return EdisClientId;
            default:
                throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null);
        }
    }

    [Theory]
    [InlineData("another application")]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("first-party token")]
    public async Task Handle_ReplayNamingAClientTheTokenWasNotIssuedTo_IsInvalidClient_BeforeAnyReuseHandling(string mismatch)
    {
        // T4: refused before anything is revoked, rotated or counted, so a wrong
        // client_id is neither a way to the grace nor a trigger for the cascade.
        var (applicationId, _, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
        var clientId = ArrangeClientMismatch(mismatch, applicationId);

        var result = await _handler.Handle(ApplicationCommand("t0", clientId), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidClient.Code);
        VerifyTheTokenWasNeverTouched();
    }

    [Theory]
    [InlineData("another application")]
    [InlineData("unknown")]
    [InlineData("inactive")]
    public async Task Handle_LiveApplicationTokenNamingAnotherClient_IsInvalidClient_AndIsNotRotated(string mismatch)
    {
        var (applicationId, _, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
        var live = TestHelpers.CreateRefreshToken(
            applicationId: applicationId, expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid());
        ArrangeRefresh("live", live);
        var clientId = ArrangeClientMismatch(mismatch, applicationId);

        var result = await _handler.Handle(ApplicationCommand("live", clientId), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.InvalidClient.Code);
        VerifyTheTokenWasNeverTouched();
    }

    [Fact]
    public async Task Handle_LiveApplicationTokenNamingItsOwnClient_RotatesAsAnyRefresh()
    {
        var (applicationId, _, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
        var live = TestHelpers.CreateRefreshToken(
            applicationId: applicationId, expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid());
        ArrangeRefresh("live", live);

        var result = await _handler.Handle(ApplicationCommand("live"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.TryRotateAsync(
                It.Is<RefreshTokenEntity>(t => t.Id == live.Id && t.ReplacedByTokenHash == "new-hash"),
                It.IsAny<RefreshTokenEntity>(),
                It.IsAny<CancellationToken>()),
            Times.Once());
        VerifyWarningLogged("ReplayGraceUsed", Times.Never());
    }

    [Theory]
    [InlineData(31, 30)]   // after the window
    [InlineData(5, 0)]     // the window switched off
    [InlineData(5, -5)]    // a negative value is off too
    [InlineData(61, 3600)] // a configured hour is brought down to the 60 s ceiling
    public async Task Handle_ApplicationReplayOutsideItsWindow_Cascades(int secondsAgo, int windowSeconds)
    {
        // T5: the setting is read per request.
        _jwtSettings.ApplicationRefreshReplayGraceSeconds = windowSeconds;
        var (_, t0, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(secondsAgo));

        var result = await _handler.Handle(ApplicationCommand("t0"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(t0.UserId);
        VerifyReuseAlert(t0.UserId);
    }

    [Fact]
    public async Task Handle_ApplicationReplay_HonoursAWiderConfiguredWindow()
    {
        _jwtSettings.ApplicationRefreshReplayGraceSeconds = 60;
        ArrangeApplicationReplay(TimeSpan.FromSeconds(45));

        var result = await _handler.Handle(ApplicationCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyNoBulkRevocationAndNoMail();
    }

    [Fact]
    public async Task Handle_FirstPartyCookieReplay_NeverGetsTheApplicationWindow()
    {
        // T6: rotated 20 s ago; the cookie window is 15 s, the application window 60 s.
        _jwtSettings.RefreshReplayGraceSeconds = 15;
        _jwtSettings.ApplicationRefreshReplayGraceSeconds = 60;
        var userId = Guid.NewGuid();
        ArrangeRefresh("t0", RotatedToken(userId, TimeSpan.FromSeconds(20), "t1-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("t1-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: userId, expiresAt: DateTime.UtcNow.AddDays(7)));

        var result = await _handler.Handle(
            CreateCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(userId);
    }

    [Fact]
    public async Task Handle_ApplicationReplay_NeverGetsTheCookieWindow_EvenClaimedFromTheCookie()
    {
        // T6: rotated 20 s ago; the application window is 15 s, the cookie window 120 s.
        _jwtSettings.RefreshReplayGraceSeconds = 120;
        _jwtSettings.ApplicationRefreshReplayGraceSeconds = 15;
        var (_, t0, _) = ArrangeApplicationReplay(TimeSpan.FromSeconds(20));

        var result = await _handler.Handle(
            ApplicationCommand("t0") with { ReplayGraceEligible = true }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        VerifyBulkRevocation(t0.UserId);
    }

    // ------------------------------------------------------------------
    // OI-103 and OI-97: the session row slides with the refresh token handed
    // out, through one guarded write that never revives an ended row.
    // ------------------------------------------------------------------

    private void ArrangeLiveSessionRow(RefreshTokenEntity token)
    {
        var sessionId = token.SessionId!.Value;
        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: sessionId, userId: token.UserId,
                createdAt: DateTime.UtcNow.AddDays(-6), expiresAt: DateTime.UtcNow.AddDays(1)));
    }

    private void VerifyTouch(RefreshTokenEntity token, DateTime expiresAt)
    {
        _sessionRepositoryMock.Verify(
            r => r.TouchOnRefreshAsync(
                token.SessionId!.Value, token.UserId, It.IsAny<DateTime>(), expiresAt, It.IsAny<CancellationToken>()),
            Times.Once());
        _sessionRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()), Times.Never(),
            "the whole-row write could undo a sign-out that landed after the read");
    }

    private void VerifyNoTouch() =>
        _sessionRepositoryMock.Verify(
            r => r.TouchOnRefreshAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never());

    [Fact]
    public async Task Handle_Rotation_SlidesTheSessionToTheExpiryOfTheTokenHandedOut()
    {
        var (_, presented) = ArrangeRefresh("t0");
        ArrangeLiveSessionRow(presented);
        RefreshTokenEntity? replacement = null;
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity _, RefreshTokenEntity created, CancellationToken _) => replacement = created)
            .ReturnsAsync(true);

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        replacement!.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        VerifyTouch(presented, replacement.ExpiresAt);
    }

    [Fact]
    public async Task Handle_LostRotationRace_SlidesTheSessionToTheSiblingsExpiry()
    {
        var (_, presented) = ArrangeRefresh("t0");
        ArrangeLiveSessionRow(presented);
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(presented.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RotatedToken(presented.UserId, TimeSpan.FromMilliseconds(5), "winner-hash"));
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync("winner-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(userId: presented.UserId, expiresAt: DateTime.UtcNow.AddDays(7)));
        RefreshTokenEntity? sibling = null;
        _refreshTokenRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity created, CancellationToken _) => sibling = created)
            .ReturnsAsync((RefreshTokenEntity created, CancellationToken _) => created);

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyTouch(presented, sibling!.ExpiresAt);
    }

    [Fact]
    public async Task Handle_ApplicationGraceAnswer_SlidesTheSessionToTheExpiryOfTheTokenHandedOut()
    {
        var (_, _, t1) = ArrangeApplicationReplay(TimeSpan.FromSeconds(5));
        ArrangeLiveSessionRow(t1);
        RefreshTokenEntity? replacement = null;
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity _, RefreshTokenEntity created, CancellationToken _) => replacement = created)
            .ReturnsAsync(true);

        var result = await _handler.Handle(ApplicationCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyTouch(t1, replacement!.ExpiresAt);
    }

    [Fact]
    public async Task Handle_RotationOff_SlidesTheSessionToThePresentedTokensOwnExpiry()
    {
        _jwtSettings.RotateRefreshTokens = false;
        var presented = TestHelpers.CreateRefreshToken(
            expiresAt: DateTime.UtcNow.AddHours(2), sessionId: Guid.NewGuid());
        ArrangeRefresh("t0", presented);
        ArrangeLiveSessionRow(presented);

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyTouch(presented, presented.ExpiresAt);
    }

    [Fact]
    public async Task Handle_EndedOrMissingSessionRow_IsNotTouched()
    {
        var (_, presented) = ArrangeRefresh("t0");
        var sessionId = presented.SessionId!.Value;
        _sessionRepositoryMock
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: sessionId, userId: presented.UserId,
                isActive: false, terminatedAt: DateTime.UtcNow.AddMinutes(-1), terminationReason: "logout"));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyNoTouch();
        _sessionRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_TheTouch_RunsWithoutTheRequestsCancellation()
    {
        // The rotation it follows is committed: the row must follow the token even
        // when the client hangs up, and must not turn that commit into an error.
        var (_, presented) = ArrangeRefresh("t0");
        ArrangeLiveSessionRow(presented);
        using var request = new CancellationTokenSource();

        var result = await _handler.Handle(CreateCommand("t0"), request.Token);

        result.IsError.Should().BeFalse();
        _sessionRepositoryMock.Verify(
            r => r.TouchOnRefreshAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.Is<CancellationToken>(t => !t.CanBeCanceled)),
            Times.Once());
    }

    [Fact]
    public async Task Handle_TouchFails_IsAWarning_NotAnError()
    {
        var (_, presented) = ArrangeRefresh("t0");
        ArrangeLiveSessionRow(presented);
        _sessionRepositoryMock
            .Setup(r => r.TouchOnRefreshAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.IsError.Should().BeFalse("the token is already rotated; the row is bookkeeping");
        result.Value.RefreshToken.Should().Be("new-refresh-token");
        VerifyWarningLogged("Failed to update session activity", Times.Once());
    }

    [Fact]
    public async Task Handle_RefusedRefresh_DoesNotTouchTheSession()
    {
        // The rotation lost to a sign-out in flight: no token is handed out, so
        // there is no expiry to slide to.
        var (_, presented) = ArrangeRefresh("t0");
        ArrangeLiveSessionRow(presented);
        _refreshTokenRepositoryMock
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByIdAsync(presented.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateRefreshToken(
                id: presented.Id, userId: presented.UserId,
                revokedAt: DateTime.UtcNow, reasonRevoked: "User logout"));

        var result = await _handler.Handle(CreateCommand("t0"), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        VerifyNoTouch();
    }
}
