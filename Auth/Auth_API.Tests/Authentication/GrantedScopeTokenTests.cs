using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Where the granted scope is written once a sign-in is issued (OI-58, B6–B7): the
/// access token's single "scope" claim, the refresh token a refresh narrows, and
/// the token response. The builder is exercised with its collaborators mocked; the
/// claim itself is read from a real <see cref="JwtTokenService"/> token, decoded
/// the way a relying party reads it, because only the payload JSON shows whether
/// the value left as a string or as an array.
/// </summary>
public sealed class GrantedScopeTokenTests : IDisposable
{
    private const string Grant = "openid profile email phone";

    private readonly Mock<IJwtTokenService> _jwtMock = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokensMock = new();
    private readonly User _user = TestHelpers.CreateUser(email: "user@example.com");

    // A real token service with settings that hold no key material, so the
    // constructor makes a throwaway in-memory key.
    private readonly JwtTokenService _service = new(
        Options.Create(new JwtSettings
        {
            Issuer = "https://auth.example.com",
            Audience = "auth-platform",
            KeyId = "test-key"
        }),
        Mock.Of<IPasswordHasher>());

    public void Dispose() => _service.Dispose();

    private LoginResponseBuilder CreateBuilder()
    {
        var claims = new Mock<ITokenClaimsResolver>();
        claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));

        _jwtMock.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(), It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-token");
        _jwtMock.Setup(s => s.GenerateRefreshToken()).Returns("refresh-token");
        _jwtMock.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns(Guid.NewGuid().ToString());

        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns("hash");

        var sessions = new Mock<IUserSessionRepository>();
        sessions.Setup(r => r.GetActiveSessionPressureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionPressure(0, null));

        return new LoginResponseBuilder(
            claims.Object,
            TestHelpers.CreatePlatformMfaPolicy(),
            _jwtMock.Object,
            keys.Object,
            _refreshTokensMock.Object,
            new Mock<IUserRepository>().Object,
            new Mock<ILoginAttemptRepository>().Object,
            sessions.Object,
            new Mock<IIdpSessionRepository>().Object,
            new Mock<IUserKnownDeviceRepository>().Object,
            new Mock<IGeoIpLookup>().Object,
            new Mock<ICredentialRevocationService>().Object,
            new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                Issuer = "test",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7
            }),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            TestHelpers.CreateOptions(new NotificationSettings { NewDeviceAlertEnabled = false }),
            TestHelpers.CreateOptions(new SessionSettings()),
            TimeProvider.System,
            new Mock<ILogger<LoginResponseBuilder>>().Object);
    }

    private void VerifyAccessTokenScope(string? scope) =>
        _jwtMock.Verify(
            s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(),
                It.IsAny<Guid?>(), It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), scope, It.IsAny<TokenOrganization?>()),
            Times.Once);

    private void VerifyStoredRefreshTokenScope(string? scope) =>
        _refreshTokensMock.Verify(
            r => r.CreateAsync(It.Is<RefreshToken>(t => t.Scope == scope), It.IsAny<CancellationToken>()),
            Times.Once);

    [Fact]
    public async Task BuildAsync_ApplicationGrant_GoesToTheAccessTokenTheRefreshTokenAndTheResponse()
    {
        var response = await CreateBuilder().BuildAsync(
            _user, "203.0.113.10", "agent", deviceId: null, AuthenticationMethods.Password, CancellationToken.None,
            establishIdpSession: false, audience: "EDIS", applicationId: Guid.NewGuid(), scope: Grant);

        response.IsError.Should().BeFalse();
        response.Value.Token!.Scope.Should().Be(Grant);
        VerifyAccessTokenScope(Grant);
        VerifyStoredRefreshTokenScope(Grant);
    }

    [Fact]
    public async Task Exchange_ApplicationNowAllowsLess_NarrowsTheTokenTheRefreshTokenAndTheResponse()
    {
        // The whole exchange through the real builder: an administrator removed
        // email and phone after the code was issued with all four scopes. The
        // access token, the stored refresh token and the response all carry the
        // narrowed grant, so the removal is honoured from the very first token.
        var verifier = new string('v', 43);
        var challenge = Convert.ToBase64String(
                System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var application = TestHelpers.CreateApplication(code: "EDIS");
        application.LoadAllowedScopes("profile");
        var code = AuthorizationCode.Create(
            application.Id, _user.Id, "code-hash", "https://edis.example.com/cb", challenge,
            TimeSpan.FromSeconds(60), "127.0.0.1", Auth.Domain.ValueObjects.ScopeSet.FromStored(Grant));

        var codes = new Mock<IAuthorizationCodeRepository>();
        codes.Setup(r => r.ConsumeByCodeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByCodeAsync("EDIS", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var access = new Mock<IApplicationAccessRepository>();
        access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns("code-hash");

        var handler = new Auth.Application.Features.Authentication.TokenExchange.ExchangeAuthorizationCodeCommandHandler(
            codes.Object, applications.Object, access.Object, users.Object, keys.Object,
            CreateBuilder(), new Mock<ICredentialRevocationService>().Object,
            new Mock<ILogger<Auth.Application.Features.Authentication.TokenExchange.ExchangeAuthorizationCodeCommandHandler>>().Object);

        var result = await handler.Handle(
            new Auth.Application.Features.Authentication.TokenExchange.ExchangeAuthorizationCodeCommand(
                "plain-code", "https://edis.example.com/cb", "EDIS", verifier, "127.0.0.1", "agent"),
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Scope.Should().Be("openid profile");
        VerifyAccessTokenScope("openid profile");
        VerifyStoredRefreshTokenScope("openid profile");
    }

    [Fact]
    public async Task BuildAsync_PlatformSignIn_CarriesNoScope()
    {
        var response = await CreateBuilder().BuildAsync(
            _user, "203.0.113.10", "agent", deviceId: null, AuthenticationMethods.Password, CancellationToken.None,
            establishIdpSession: false);

        response.Value.Token!.Scope.Should().BeNull();
        VerifyAccessTokenScope(null);
        VerifyStoredRefreshTokenScope(null);
    }

    [Fact]
    public void TokenResponse_PlatformSignIn_PutsNoScopeOnTheWire()
    {
        // The first-party login and refresh responses are serialized with the
        // API's global options, which drop nulls: a platform sign-in's body is
        // exactly what it was before scopes existed.
        var json = JsonSerializer.Serialize(
            new TokenResponse { AccessToken = "a", RefreshToken = "r", ExpiresIn = 1, RefreshExpiresIn = 2 },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

        json.Should().NotContain("scope");
    }

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment)));
        return document.RootElement.Clone();
    }

    [Fact]
    public void GenerateAccessToken_ApplicationGrant_WritesOneSpaceDelimitedScopeString()
    {
        var token = _service.GenerateAccessToken(
            _user, [], [], AccessTokenAuthentication.Unrecorded, sessionId: null, organizationPermissions: null, audience: "EDIS", scope: "openid phone");
        var payload = DecodePayload(token);

        // RFC 9068 §2.2.3: one string, not one claim per scope (which the handler
        // would serialize as a JSON array).
        payload.GetProperty("scope").ValueKind.Should().Be(JsonValueKind.String);
        payload.GetProperty("scope").GetString().Should().Be("openid phone");
        payload.GetProperty("aud").GetString().Should().Be("EDIS");
    }

    [Fact]
    public void GenerateAccessToken_PlatformToken_HasNoScopeClaim()
    {
        var payload = DecodePayload(_service.GenerateAccessToken(_user, ["users:read"], ["Admin"], AccessTokenAuthentication.Unrecorded));

        payload.TryGetProperty("scope", out _).Should().BeFalse();
    }

    [Fact]
    public void GenerateAccessToken_WithScope_ChangesNoOtherClaim()
    {
        // B15: the scope is added, nothing is filtered or renamed.
        var withScope = DecodePayload(_service.GenerateAccessToken(
            _user, ["users:read"], ["Admin"], AccessTokenAuthentication.Unrecorded, sessionId: null, organizationPermissions: null,
            audience: "EDIS", scope: Grant));
        var without = DecodePayload(_service.GenerateAccessToken(
            _user, ["users:read"], ["Admin"], AccessTokenAuthentication.Unrecorded, sessionId: null, organizationPermissions: null,
            audience: "EDIS"));

        var withNames = withScope.EnumerateObject().Select(p => p.Name).ToList();
        var withoutNames = without.EnumerateObject().Select(p => p.Name).ToList();

        withNames.Except(withoutNames).Should().Equal("scope");
        withoutNames.Except(withNames).Should().BeEmpty();
        foreach (var name in withoutNames.Except(["jti", "iat", "nbf", "exp"]))
        {
            withScope.GetProperty(name).GetRawText().Should().Be(
                without.GetProperty(name).GetRawText(), $"the '{name}' claim must not change with a scope");
        }
    }
}
