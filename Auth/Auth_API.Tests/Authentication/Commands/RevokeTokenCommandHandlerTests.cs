using Auth.Application.Features.Authentication.RevokeToken;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The revocation endpoint's handler (RFC 7009), with the REAL token validator the bearer-scheme
/// registration builds and real tokens: an access token is blacklisted by its jti, a refresh token
/// with a session ends that session, and nothing else is stored.
/// </summary>
public sealed class RevokeTokenCommandHandlerTests : IDisposable
{
    private readonly UserInfoTokens _tokens = new();
    private readonly User _user = TestHelpers.CreateUser();
    private readonly ServiceProvider _provider;
    private readonly Mock<ITokenBlacklistService> _tokenBlacklistServiceMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = new();
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        var services = new ServiceCollection();
        services.AddAuthSystemBearerSchemes(_tokens.Settings, _tokens.Key);
        _provider = services.BuildServiceProvider();

        _handler = new RevokeTokenCommandHandler(
            _provider.GetRequiredService<IIssuedAccessTokenValidator>(),
            _tokenBlacklistServiceMock.Object,
            _refreshTokenRepositoryMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _credentialRevocationMock.Object,
            new Mock<ILogger<RevokeTokenCommandHandler>>().Object);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _tokens.Dispose();
    }

    private void SetupStoredRefreshToken(string plain, RefreshTokenEntity? stored)
    {
        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash(plain))
            .Returns($"hash:{plain}");
        _refreshTokenRepositoryMock
            .Setup(r => r.GetByTokenHashAsync($"hash:{plain}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
    }

    private void VerifyNothingWritten()
    {
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _credentialRevocationMock.VerifyNoOtherCalls();
        _tokenBlacklistServiceMock.VerifyNoOtherCalls();
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

    // --- Access tokens: the jti, nothing more ---

    [Fact]
    public async Task Handle_ApplicationAccessToken_BlacklistsItsJti()
    {
        // The token the platform-only check used to answer 200 and leave working.
        var token = _tokens.ForApplication(_user, "openid profile");

        var result = await _handler.Handle(
            new RevokeTokenCommand(token, TokenTypeHint.AccessToken, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken(_tokens.Service.GetTokenId(token)!, It.IsAny<DateTime>()),
            Times.Once());
        // The session stays up: a leaked access token must not be enough to sign its owner out.
        _credentialRevocationMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_PlatformAccessToken_BlacklistsItsJti()
    {
        var token = _tokens.ForPlatform(_user);

        var result = await _handler.Handle(
            new RevokeTokenCommand(token, TokenTypeHint.AccessToken, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken(_tokens.Service.GetTokenId(token)!, It.IsAny<DateTime>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_ValidAccessToken_BlacklistsUntilItsOwnExpiry()
    {
        // The entry lives as long as the token would have: seven minutes here, not the
        // one-hour fallback.
        var exp = DateTimeOffset.UtcNow.AddMinutes(7);
        var token = _tokens.Custom(_user.Id, descriptor => descriptor.Expires = exp.UtcDateTime);

        await _handler.Handle(new RevokeTokenCommand(token, TokenTypeHint.AccessToken, null), CancellationToken.None);

        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken(
                It.IsAny<string>(),
                It.Is<DateTime>(d => Math.Abs((d - exp.UtcDateTime).TotalSeconds) < 1)),
            Times.Once());
    }

    [Fact]
    public async Task Handle_AccessTokenThatFailsValidation_StoresNothingAndAnswers200()
    {
        // Forged, expired or malformed: the full catalogue is IssuedAccessTokenValidatorTests.
        var command = new RevokeTokenCommand("header.payload.forged-signature", TokenTypeHint.AccessToken, null);

        var result = await _handler.Handle(command, CancellationToken.None);

        // RFC 7009: 200 either way, but nothing pinned
        result.IsError.Should().BeFalse();
        VerifyNothingWritten();
    }

    [Fact]
    public async Task Handle_NoTypeHint_JwtTokenDetectedAsAccessToken()
    {
        // Arrange — token with dots = JWT = access token
        var token = _tokens.ForApplication(_user, "openid");

        // Act
        var result = await _handler.Handle(new RevokeTokenCommand(token, null, null), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        _tokenBlacklistServiceMock.Verify(
            s => s.BlacklistToken(_tokens.Service.GetTokenId(token)!, It.IsAny<DateTime>()),
            Times.Once());
    }

    // --- Refresh tokens: the whole session, when there is one ---

    [Fact]
    public async Task Handle_RefreshTokenWithASession_EndsThatSession()
    {
        // RFC 7009 2.1: the access tokens of the same grant go too. The session is the
        // grant; ending it revokes its refresh tokens and blacklists its id.
        var sessionId = Guid.NewGuid();
        var revokedBy = Guid.NewGuid();
        var stored = TestHelpers.CreateRefreshToken(userId: _user.Id, sessionId: sessionId);
        SetupStoredRefreshToken("refresh-token-value", stored);
        using var cancellation = new CancellationTokenSource();

        var result = await _handler.Handle(
            new RevokeTokenCommand("refresh-token-value", TokenTypeHint.RefreshToken, revokedBy),
            cancellation.Token);

        result.IsError.Should().BeFalse();
        _refreshTokenRepositoryMock.Verify(
            r => r.GetByTokenHashAsync("hash:refresh-token-value", cancellation.Token), Times.Once());
        _credentialRevocationMock.Verify(
            c => c.TerminateSessionAsync(
                sessionId, revokedBy, TokenRevocationReasons.RevocationRequested, cancellation.Token),
            Times.Once());
        _credentialRevocationMock.VerifyNoOtherCalls();
        // The service revokes the session's refresh tokens, this one included.
        _refreshTokenRepositoryMock.Verify(
            r => r.UpdateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_SessionlessRefreshToken_RevokesOnlyItsOwnRow()
    {
        var revokedBy = Guid.NewGuid();
        var stored = TestHelpers.CreateRefreshToken(userId: _user.Id, sessionId: null);
        SetupStoredRefreshToken("legacy-token", stored);

        var result = await _handler.Handle(
            new RevokeTokenCommand("legacy-token", TokenTypeHint.RefreshToken, revokedBy), CancellationToken.None);

        result.IsError.Should().BeFalse();
        stored.IsRevoked.Should().BeTrue();
        stored.RevokedBy.Should().Be(revokedBy);
        stored.ReasonRevoked.Should().Be(TokenRevocationReasons.RevocationRequested);
        _refreshTokenRepositoryMock.Verify(r => r.UpdateAsync(stored, It.IsAny<CancellationToken>()), Times.Once());
        _credentialRevocationMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void RevocationRequested_KeepsTheReasonTheEndpointHasAlwaysWritten()
    {
        // Stored reasons stay continuous across the change: operators triage by this text.
        TokenRevocationReasons.RevocationRequested.Should().Be("Token revocation requested");
    }

    [Fact]
    public async Task Handle_NonExistentRefreshToken_ReturnsSuccessPerRfc7009_AndWritesNothing()
    {
        SetupStoredRefreshToken("unknown-token", null);

        var result = await _handler.Handle(
            new RevokeTokenCommand("unknown-token", TokenTypeHint.RefreshToken, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyNothingWritten();
    }

    public static TheoryData<bool> WithAndWithoutSession => new() { true, false };

    [Fact]
    public void WithAndWithoutSession_IsNotEmpty() =>
        WithAndWithoutSession.Count<object[]>().Should().BeGreaterThan(0);

    [Theory]
    [MemberData(nameof(WithAndWithoutSession))]
    public async Task Handle_AlreadyRevokedRefreshToken_ReturnsSuccess_AndWritesNothing(bool hasSession)
    {
        var stored = TestHelpers.CreateRefreshToken(
            userId: _user.Id,
            sessionId: hasSession ? Guid.NewGuid() : null,
            revokedAt: DateTime.UtcNow.AddMinutes(-5),
            revokedBy: Guid.NewGuid(),
            reasonRevoked: "Already revoked");
        SetupStoredRefreshToken("revoked-token", stored);

        var result = await _handler.Handle(
            new RevokeTokenCommand("revoked-token", TokenTypeHint.RefreshToken, null), CancellationToken.None);

        result.IsError.Should().BeFalse();
        VerifyNothingWritten();
    }
}
