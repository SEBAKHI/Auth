using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Features.MachineCredentialChecks;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Common.Authentication;
using Auth_API.Tests.Authentication.OidcUserInfo;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using AppEntity = Auth.Domain.Entities.Application;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Queries;

/// <summary>
/// Introspection answers an application's own server about its own tokens only (OI-128 PR B,
/// owner decision D-128-6 (a)): an access token issued to the caller, validated by the userinfo
/// scheme's own parameters and reached by no revocation, or a refresh token of the caller's. Every
/// other token, a platform token included, is inactive. Real tokens from the real token service,
/// validated by the real <see cref="ApplicationAccessTokenValidator"/> over the userinfo profile.
/// </summary>
public sealed class IntrospectOwnTokensTests : IDisposable
{
    private readonly UserInfoTokens _tokens = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly IntrospectTokenQueryHandler _handler;

    private readonly Auth.Domain.Entities.User _user = TestHelpers.CreateUser();
    private readonly AppEntity _caller = TestHelpers.CreateApplication(code: UserInfoTokens.Application.ToUpperInvariant());
    private readonly AppEntity _other = TestHelpers.CreateApplication(code: "OTHER");

    public IntrospectOwnTokensTests()
    {
        _applications
            .Setup(r => r.GetByCodeAsync(_caller.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_caller);

        _handler = new IntrospectTokenQueryHandler(
            new ApplicationAccessTokenValidator(_tokens.UserInfoParameters),
            _applications.Object,
            _blacklist.Object,
            _refreshTokens.Object,
            new FakeCredentialHasher(),
            NullLogger<IntrospectTokenQueryHandler>.Instance);
    }

    public void Dispose() => _tokens.Dispose();

    private async Task<ErrorOr<IntrospectTokenResponse>> Introspect(
        string token,
        TokenTypeHint? hint = TokenTypeHint.AccessToken,
        string? scope = MachineClients.Scopes.TokensIntrospect) =>
        await _handler.Handle(
            new IntrospectTokenQuery(new MachineCredentialCaller(_caller.Code, scope), token, hint),
            CancellationToken.None);

    [Fact]
    public async Task Handle_OwnApplicationsAccessToken_IsActive_WithWhatTheTokenCarries()
    {
        var response = (await Introspect(_tokens.ForApplication(_user, "openid profile"))).Value;

        response.Active.Should().BeTrue();
        response.Sub.Should().Be(_user.Id.ToString());
        response.Aud.Should().Be(UserInfoTokens.Application);
        response.ClientId.Should().Be(UserInfoTokens.Application);
        response.Scope.Should().Be("openid profile");
        response.Jti.Should().NotBeNullOrEmpty();
        response.Exp.Should().NotBeNull();
        response.Roles.Should().Equal("customer");
        response.Permissions.Should().Equal("shop:customers:read");
    }

    [Fact]
    public async Task Handle_AnotherApplicationsAccessToken_IsInactive()
    {
        var response = (await Introspect(_tokens.ForApplication(_user, "openid", audience: "other"))).Value;

        response.Active.Should().BeFalse("a token issued to another application is not the caller's business");
        response.Sub.Should().BeNull("an inactive answer carries nothing else (RFC 7662 §2.2)");
    }

    [Fact]
    public async Task Handle_APlatformAccessToken_IsInactive()
    {
        (await Introspect(_tokens.ForPlatform(_user))).Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OwnAccessToken_AfterItsSessionEnded_IsInactive()
    {
        var sessionId = Guid.NewGuid();
        var token = _tokens.ForApplication(_user, "openid", sessionId: sessionId);
        (await Introspect(token)).Value.Active.Should().BeTrue();

        _blacklist.Setup(b => b.IsSessionBlacklisted(sessionId.ToString())).Returns(true);

        (await Introspect(token)).Value.Active.Should().BeFalse("the platform's own API refuses it from the sign-out on");
    }

    [Fact]
    public async Task Handle_OwnAccessToken_AfterTheUsersSignOutOfEverything_IsInactive()
    {
        var token = _tokens.ForApplication(_user, "openid");
        _blacklist.Setup(b => b.AreUserTokensBlacklisted(_user.Id, It.IsAny<DateTime>())).Returns(true);

        (await Introspect(token)).Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OwnAccessToken_WhoseJtiIsBlacklisted_IsInactive()
    {
        _blacklist.Setup(b => b.IsTokenBlacklisted(It.IsAny<string>())).Returns(true);

        (await Introspect(_tokens.ForApplication(_user, "openid"))).Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OwnRefreshToken_IsActive_AndAnotherClientsIsNot()
    {
        var own = TestHelpers.CreateRefreshToken(userId: _user.Id, applicationId: _caller.Id);
        var other = TestHelpers.CreateRefreshToken(applicationId: _other.Id);
        var platform = TestHelpers.CreateRefreshToken(applicationId: null);
        var revoked = TestHelpers.CreateRefreshToken(applicationId: _caller.Id, revokedAt: DateTime.UtcNow.AddMinutes(-1));
        var expired = TestHelpers.CreateRefreshToken(applicationId: _caller.Id, expiresAt: DateTime.UtcNow.AddMinutes(-1));
        var byToken = new Dictionary<string, RefreshTokenEntity>
        {
            ["own"] = own, ["other"] = other, ["platform"] = platform, ["revoked"] = revoked, ["expired"] = expired,
        };
        _refreshTokens
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<Auth.Domain.ValueObjects.CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.ValueObjects.CredentialHashCandidates candidates, CancellationToken _) =>
                byToken.FirstOrDefault(pair => candidates.ValuesFor(CredentialPurpose.RefreshToken)
                    .Contains(new FakeCredentialHasher().Hash(CredentialPurpose.RefreshToken, pair.Key))).Value);

        var active = (await Introspect("own", TokenTypeHint.RefreshToken)).Value;
        active.Active.Should().BeTrue();
        active.Sub.Should().Be(_user.Id.ToString());
        active.ClientId.Should().Be(_caller.Code);

        foreach (var token in new[] { "other", "platform", "revoked", "expired", "unknown" })
        {
            (await Introspect(token, TokenTypeHint.RefreshToken)).Value.Active.Should().BeFalse(token);
        }
    }

    [Fact]
    public async Task Handle_EmptyToken_IsInactive()
    {
        (await Introspect(string.Empty, hint: null)).Value.Active.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CallerWithoutTheScope_IsRefused_BeforeTheTokenIsRead()
    {
        var result = await Introspect(_tokens.ForApplication(_user, "openid"), scope: MachineClients.Scopes.ApiKeysValidate);

        result.FirstError.Code.Should().Be("Http.Forbidden");
        _blacklist.VerifyNoOtherCalls();
        _refreshTokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_CallerWhoseApplicationIsInactive_IsTheRevokedTokenAnswer()
    {
        _applications
            .Setup(r => r.GetByCodeAsync(_caller.Code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(code: _caller.Code, isActive: false));

        (await Introspect(_tokens.ForApplication(_user, "openid"))).FirstError.Code.Should().Be("Http.TokenRevoked");
    }
}
