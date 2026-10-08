using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-63 B7: an application token names the one organization in which the user
/// holds the application's delegated permissions, as <c>org_id</c> and
/// <c>org_name</c>. Read from real tokens (an in-memory key, no real user),
/// decoded the way a relying party reads them, at BOTH mint sites: a claim
/// minted at sign-in only would vanish at the first refresh.
/// </summary>
public sealed class OrganizationClaimTokenTests : IDisposable
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private const string OrganizationName = "مؤسسة المعارض <b>";

    private readonly ITestOutputHelper _output;
    private readonly User _user = TestHelpers.CreateUser(email: "exhibitor@example.com");
    private readonly Mock<ITokenClaimsResolver> _claims = new();

    private readonly JwtTokenService _service = new(
        Options.Create(new JwtSettings
        {
            Issuer = "https://auth.example.com",
            Audience = "auth-platform",
            KeyId = "test-key"
        }),
        Mock.Of<IPasswordHasher>());

    public OrganizationClaimTokenTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _service.Dispose();

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment)));
        return document.RootElement.Clone();
    }

    private void ResolvesOneOrganization() =>
        _claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims(
                [], [], [(OrganizationId, "edis:exhibitors:manage")],
                new TokenOrganization(OrganizationId, OrganizationName)));

    private static void ShouldNameTheOrganization(JsonElement payload)
    {
        payload.GetProperty("org_id").ValueKind.Should().Be(JsonValueKind.String);
        payload.GetProperty("org_id").GetString().Should().Be(OrganizationId.ToString());
        payload.GetProperty("org_name").ValueKind.Should().Be(JsonValueKind.String);
        payload.GetProperty("org_name").GetString().Should().Be(OrganizationName,
            "the name is carried as typed; encoding it is the relying party's job");
    }

    [Fact]
    public void AnOrganization_IsWrittenAsTwoStrings()
    {
        var token = _service.GenerateAccessToken(
            _user, [], [], AccessTokenAuthentication.Unrecorded, Guid.NewGuid(), [(OrganizationId, "edis:exhibitors:manage")],
            audience: "EDIS", scope: "openid", organization: new TokenOrganization(OrganizationId, OrganizationName));
        var payload = DecodePayload(token);

        ShouldNameTheOrganization(payload);
        payload.GetProperty("org_perm").GetString().Should().Be($"{OrganizationId}:edis:exhibitors:manage");

        // Evidence for the PR: the decoded payload of an application token.
        _output.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
    }

    [Fact]
    public void NoOrganization_WritesNeitherClaim()
    {
        var payload = DecodePayload(_service.GenerateAccessToken(_user, [], [], AccessTokenAuthentication.Unrecorded, audience: "EDIS", scope: "openid"));

        payload.TryGetProperty("org_id", out _).Should().BeFalse();
        payload.TryGetProperty("org_name", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SignIn_ThroughTheRealBuilder_NamesTheOrganization()
    {
        ResolvesOneOrganization();

        var response = await CreateBuilder().BuildAsync(
            _user, "203.0.113.10", "agent", deviceId: null, AuthenticationMethods.Password, CancellationToken.None,
            establishIdpSession: false, audience: "EDIS", applicationId: Guid.NewGuid(), scope: "openid");

        ShouldNameTheOrganization(DecodePayload(response.Value.Token!.AccessToken));
    }

    [Fact]
    public async Task Refresh_ThroughTheRealHandler_NamesTheOrganizationToo()
    {
        ResolvesOneOrganization();
        var application = TestHelpers.CreateApplication(code: "EDIS");
        var stored = TestHelpers.CreateRefreshToken(
            userId: _user.Id, applicationId: application.Id, expiresAt: DateTime.UtcNow.AddDays(7));

        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns("hash");
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens.Setup(r => r.GetByTokenHashAsync("hash", It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        refreshTokens
            .Setup(r => r.TryRotateAsync(It.IsAny<RefreshToken>(), It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByIdAsync(application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var access = new Mock<IApplicationAccessRepository>();
        access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new RefreshTokenCommandHandler(
            users.Object, refreshTokens.Object, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(), applications.Object, access.Object,
            _service, keys.Object, new Mock<IUserSessionRepository>().Object, new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7, RotateRefreshTokens = true
            }),
            new Mock<ILogger<RefreshTokenCommandHandler>>().Object);

        var result = await handler.Handle(new RefreshTokenCommand("plain", "127.0.0.1", "agent"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        ShouldNameTheOrganization(DecodePayload(result.Value.AccessToken));
    }

    private LoginResponseBuilder CreateBuilder()
    {
        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns("hash");
        var sessions = new Mock<IUserSessionRepository>();
        sessions.Setup(r => r.GetActiveSessionPressureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionPressure(0, null));

        return new LoginResponseBuilder(
            _claims.Object,
            TestHelpers.CreatePlatformMfaPolicy(),
            _service,
            keys.Object,
            new Mock<IRefreshTokenRepository>().Object,
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
}

/// <summary>
/// The rule behind <c>org_id</c>: exactly one organization holding the
/// application's delegated codes, application tokens only.
/// </summary>
public class TokenClaimsResolverOrganizationTests
{
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IOrganizationRepository> _organizations = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _applicationId = Guid.NewGuid();

    public TokenClaimsResolverOrganizationTests()
    {
        _roles.Setup(r => r.GetUserRolesForApplicationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _roles.Setup(r => r.GetUserRolesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _permissions.Setup(r => r.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _organizations.Setup(r => r.GetMembershipPermissionCodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _organizations
            .Setup(r => r.GetMembershipPermissionCodesForApplicationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private TokenClaimsResolver Resolver() => new(_roles.Object, _permissions.Object, _organizations.Object);

    private void Delegated(params (Guid OrganizationId, string Code)[] pairs) =>
        _organizations
            .Setup(r => r.GetEffectivePermissionPairsForApplicationAsync(_userId, _applicationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pairs);

    private Organization Exists(Guid id, string name, bool isActive = true)
    {
        var organization = TestHelpers.CreateOrganization(id: id, name: name, isActive: isActive);
        _organizations.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(organization);
        return organization;
    }

    [Fact]
    public async Task OneOrganization_IsNamed()
    {
        var a = Guid.NewGuid();
        Exists(a, "Org A");
        Delegated((a, "edis:x"), (a, "edis:y"));

        var claims = await Resolver().ResolveAsync(_userId, _applicationId, CancellationToken.None);

        claims.Organization.Should().Be(new TokenOrganization(a, "Org A"));
    }

    [Fact]
    public async Task TwoOrganizations_NameNeither()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Exists(a, "Org A");
        Exists(b, "Org B");
        Delegated((a, "edis:x"), (b, "edis:x"));

        var claims = await Resolver().ResolveAsync(_userId, _applicationId, CancellationToken.None);

        claims.Organization.Should().BeNull();
    }

    [Fact]
    public async Task MembershipAuthorityAlone_NamesNothing()
    {
        var a = Guid.NewGuid();
        Exists(a, "Org A");
        Delegated((a, "org:members:read"));

        var claims = await Resolver().ResolveAsync(_userId, _applicationId, CancellationToken.None);

        claims.Organization.Should().BeNull();
    }

    [Fact]
    public async Task AnOrganizationGoneInactive_IsNotNamed()
    {
        var a = Guid.NewGuid();
        Exists(a, "Org A", isActive: false);
        Delegated((a, "edis:x"));

        var claims = await Resolver().ResolveAsync(_userId, _applicationId, CancellationToken.None);

        claims.Organization.Should().BeNull();
    }

    [Fact]
    public async Task APlatformToken_NamesNoOrganization()
    {
        var claims = await Resolver().ResolveAsync(_userId, applicationId: null, CancellationToken.None);

        claims.Organization.Should().BeNull();
        _organizations.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
