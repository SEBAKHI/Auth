using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Security;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS10 (PR A half), F28: a refresh token issued before a rotation (legacy form,
/// key 1) was rotated after it, so its replacement is stored under key 2's subkey.
/// A lost-response replay of the old token must still be answered from the replay
/// grace: the replacement is found by the pointer AS STORED, never by re-deriving.
/// </summary>
public class RefreshReplayGraceAcrossKeysTests
{
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly KeyringCredentialHasher _beforeRotation = TestCredentialHashers.Over(KeyringTestRings.SingleKey());
    private readonly KeyringCredentialHasher _afterRotation = TestCredentialHashers.Over(
        KeyringTestRings.Rotated(), new FixedTimeProvider(KeyringTestRings.Cutover.AddMinutes(1)));
    private readonly RefreshTokenCommandHandler _handler;
    private readonly Auth.Domain.Entities.User _user = TestHelpers.CreateUser();

    public RefreshReplayGraceAcrossKeysTests()
    {
        var claims = new Mock<ITokenClaimsResolver>();
        claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        var sessions = new Mock<IUserSessionRepository>();

        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _jwt.Setup(s => s.GenerateAccessToken(
                _user,
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("new-access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns("t2");
        _jwt.Setup(s => s.GetTokenId("new-access-token")).Returns("new-jti");

        _handler = new RefreshTokenCommandHandler(
            _users.Object,
            _tokens,
            claims.Object,
            TestHelpers.CreatePlatformMfaPolicy(),
            new Mock<IApplicationRepository>().Object,
            new Mock<IApplicationAccessRepository>().Object,
            _jwt.Object,
            _afterRotation,
            sessions.Object,
            _revocation.Object,
            new Mock<IIdpSessionReader>().Object,
            Mock.Of<IClientAuthenticator>(),
            new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7,
                RotateRefreshTokens = true
            }),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            NullLogger<RefreshTokenCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReplayOfALegacyToken_WhoseReplacementIsUnderASubkey_IsAnsweredFromTheGrace()
    {
        var sessionId = Guid.NewGuid();
        var t1Stored = _afterRotation.Hash(CredentialPurpose.RefreshToken, "t1");
        var t0 = TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: _beforeRotation.Hash(CredentialPurpose.RefreshToken, "t0"),
            expiresAt: DateTime.UtcNow.AddDays(7), sessionId: sessionId,
            revokedAt: DateTime.UtcNow.AddSeconds(-5), revokedBy: _user.Id,
            reasonRevoked: TokenRevocationReasons.Rotated, replacedByTokenHash: t1Stored);
        var t1 = TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: t1Stored, expiresAt: DateTime.UtcNow.AddDays(7), sessionId: sessionId);
        _tokens.Seed(t0);
        _tokens.Seed(t1);
        t1Stored.Should().NotBe(_beforeRotation.Hash(CredentialPurpose.RefreshToken, "t1"), "the two keys really differ");

        var result = await _handler.Handle(
            new RefreshTokenCommand("t0", "127.0.0.1", "TestAgent/1.0") { ReplayGraceEligible = true },
            CancellationToken.None);

        result.IsError.Should().BeFalse("the replay of a lost response is answered from the grace");
        result.Value.RefreshToken.Should().Be("t2");
        _revocation.Verify(
            r => r.RevokeAllCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never(), "no reuse detection: the replacement was found where it was stored");
        _tokens.ByHash(t1Stored).IsRevoked.Should().BeTrue("the grace spends the replacement");
        _tokens.ByHash(_afterRotation.Hash(CredentialPurpose.RefreshToken, "t2")).IsRevoked.Should().BeFalse(
            "the answer is a new token, fingerprinted under the issuing key");
    }

    [Fact]
    public async Task Handle_ALegacyTokenPresentedAfterTheCutover_StillRotates_IntoASubkeyToken()
    {
        var t0 = TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: _beforeRotation.Hash(CredentialPurpose.RefreshToken, "t0"),
            expiresAt: DateTime.UtcNow.AddDays(7), sessionId: Guid.NewGuid());
        _tokens.Seed(t0);

        var result = await _handler.Handle(new RefreshTokenCommand("t0", "127.0.0.1", "TestAgent/1.0"), CancellationToken.None);

        result.IsError.Should().BeFalse("a session signed in before the rotation keeps refreshing");
        var replacement = _tokens.ByHash(_afterRotation.Hash(CredentialPurpose.RefreshToken, "t2"));
        _tokens.ByHash(t0.TokenHash).ReplacedByTokenHash.Should().Be(replacement.TokenHash);
    }
}
