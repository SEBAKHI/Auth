using System.Text;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// S08 T1–T3 at the sign-in mint site: every sign-in exit records what it proved on
/// the session row and the SSO session, the token's auth_time is the session's start
/// (one clock read), and the platform-administrator decision shapes the token and
/// the user info. The token is a real one, read from its payload.
/// </summary>
public sealed class LoginResponseBuilderMethodTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizationId = Guid.NewGuid();

    private static readonly TokenClaims AdminClaims = new(
        ["super-admin"], ["*"], [(OrganizationId, "org:members:read")]);

    private static readonly TokenClaims PlainClaims = new(
        ["user"], [], [(OrganizationId, "org:members:read")]);

    private readonly User _user = TestHelpers.CreateUser(email: "admin@example.com", twoFactorEnabled: false);
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly List<UserSession> _sessionRows = [];
    private readonly List<IdpSession> _idpRows = [];

    private readonly JwtTokenService _service = new(
        Options.Create(new JwtSettings
        {
            Issuer = "https://auth.example.com",
            Audience = "auth-platform",
            KeyId = "test-key"
        }),
        Mock.Of<IPasswordHasher>());

    public LoginResponseBuilderMethodTests()
    {
        _sessions.Setup(r => r.GetActiveSessionPressureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionPressure(0, null));
        _sessions.Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback<UserSession, CancellationToken>((row, _) => _sessionRows.Add(row))
            .ReturnsAsync((UserSession row, CancellationToken _) => row);
        _idpSessions.Setup(r => r.CreateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .Callback<IdpSession, CancellationToken>((row, _) => _idpRows.Add(row))
            .ReturnsAsync((IdpSession row, CancellationToken _) => row);
    }

    public void Dispose() => _service.Dispose();

    /// <summary>A clock that moves one second every time it is read.</summary>
    private sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private int _reads;
        public int Reads => _reads;
        public override DateTimeOffset GetUtcNow() => start.AddSeconds(Interlocked.Increment(ref _reads) - 1);
    }

    private static JsonElement DecodePayload(string token)
    {
        var segment = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(segment)));
        return document.RootElement.Clone();
    }

    private LoginResponseBuilder Builder(IPlatformMfaPolicy policy, TimeProvider clock)
    {
        var keys = new Mock<IRefreshTokenKeyService>();
        keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns<string>(value => "hash:" + value.Length);

        return new LoginResponseBuilder(
            _claims.Object,
            policy,
            _service,
            keys.Object,
            new Mock<IRefreshTokenRepository>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<ILoginAttemptRepository>().Object,
            _sessions.Object,
            _idpSessions.Object,
            new Mock<IUserKnownDeviceRepository>().Object,
            new Mock<IGeoIpLookup>().Object,
            new Mock<ICredentialRevocationService>().Object,
            new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                Issuer = "https://auth.example.com",
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7
            }),
            TestHelpers.CreateOptions(new IdentityProviderSettings()),
            TestHelpers.CreateOptions(new NotificationSettings { NewDeviceAlertEnabled = false }),
            TestHelpers.CreateOptions(new SessionSettings()),
            clock,
            new Mock<ILogger<LoginResponseBuilder>>().Object);
    }

    private void Resolves(TokenClaims claims) =>
        _claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claims);

    private async Task<LoginResponse> SignIn(
        AuthenticationMethods methods,
        IPlatformMfaPolicy? policy = null,
        TimeProvider? clock = null,
        Guid? applicationId = null)
    {
        var result = await Builder(policy ?? TestHelpers.CreatePlatformMfaPolicy(), clock ?? new SteppingTimeProvider(Start))
            .BuildAsync(_user, "203.0.113.10", "agent", "device-1", methods, CancellationToken.None,
                applicationId: applicationId, audience: applicationId is null ? null : "EDIS");
        result.IsError.Should().BeFalse();
        return result.Value;
    }

    [Fact]
    public async Task BuildAsync_RecordsTheMethods_OnTheSessionRow_AndTheSsoSession()
    {
        Resolves(PlainClaims);
        var methods = AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

        await SignIn(methods);

        _sessionRows.Should().ContainSingle().Which.AuthMethods.Should().Be(9, "Password (1) | Totp (8)");
        _idpRows.Should().ContainSingle().Which.AuthMethods.Should().Be(9);
    }

    [Fact]
    public async Task BuildAsync_AuthTime_IsTheSessionStart_FromOneClockRead()
    {
        // The clock moves a second per read: a second read for the token would put
        // auth_time one second after the session's start, and the refresh — which
        // reads the start back — would then disagree with the sign-in.
        Resolves(PlainClaims);
        var clock = new SteppingTimeProvider(Start);

        var response = await SignIn(AuthenticationMethods.Password, clock: clock);

        var payload = DecodePayload(response.Token!.AccessToken);
        var startedAt = _sessionRows.Should().ContainSingle().Subject.CreatedAt;
        payload.GetProperty("auth_time").GetInt64()
            .Should().Be(new DateTimeOffset(startedAt, TimeSpan.Zero).ToUnixTimeSeconds());
        clock.Reads.Should().Be(1, "now is read once, before the mint");
        payload.GetProperty("amr").EnumerateArray().Select(v => v.GetString()).Should().Equal("pwd");
    }

    [Fact]
    public async Task BuildAsync_Enforced_WithholdsFromTheTokenAndTheUserInfo()
    {
        Resolves(AdminClaims);
        var policy = TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: false);

        var response = await SignIn(AuthenticationMethods.Password, policy);

        var payload = DecodePayload(response.Token!.AccessToken);
        payload.TryGetProperty("permissions", out _).Should().BeFalse();
        payload.TryGetProperty("roles", out _).Should().BeFalse();
        payload.GetProperty("org_perm").GetString().Should().Be($"{OrganizationId}:org:members:read",
            "org_perm is kept");
        payload.GetProperty("mfa_req").GetString().Should().Be("enroll");

        response.User!.Permissions.Should().BeEmpty();
        response.User.Roles.Should().BeEmpty();
        response.User.MfaRequirement.Should().Be("enroll");
    }

    [Fact]
    public async Task BuildAsync_AccountFlagOff_FactorRowOn_StepsUp_NotEnrols()
    {
        // The two sources of truth disagree: the account flag says off (so no
        // challenge at sign-in) while the factor row is enabled. The policy reads
        // the row, so this administrator steps up — which works — instead of being
        // sent to enrol, which setup refuses for an enabled row.
        _user.TwoFactorEnabled.Should().BeFalse();
        Resolves(AdminClaims);
        var policy = TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true);

        var response = await SignIn(AuthenticationMethods.Password, policy);

        response.User!.MfaRequirement.Should().Be("step_up");
        DecodePayload(response.Token!.AccessToken).GetProperty("mfa_req").GetString().Should().Be("step_up");
    }

    [Fact]
    public async Task BuildAsync_Enforced_MfaSession_KeepsEverything()
    {
        Resolves(AdminClaims);
        var policy = TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true);

        var response = await SignIn(AuthenticationMethods.Password.With(AuthenticationMethods.Totp), policy);

        var payload = DecodePayload(response.Token!.AccessToken);
        payload.GetProperty("permissions").GetString().Should().Be("*");
        payload.TryGetProperty("mfa_req", out _).Should().BeFalse();
        response.User!.MfaRequirement.Should().Be("none");
        response.User.Permissions.Should().Equal("*");
    }

    [Fact]
    public async Task BuildAsync_ApplicationToken_ForAPlatformAdministrator_IsNeverWithheld()
    {
        // Owner rule: application tokens are never touched — an administrator
        // without a second factor signing in to an application keeps that
        // application's permissions, and the token claims no authentication time.
        Resolves(new TokenClaims(["exhibitor"], ["edis:exhibitors:read"], []));
        var policy = TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: false);

        var response = await SignIn(AuthenticationMethods.Unknown, policy, applicationId: Guid.NewGuid());

        var payload = DecodePayload(response.Token!.AccessToken);
        payload.GetProperty("permissions").GetString().Should().Be("edis:exhibitors:read");
        payload.TryGetProperty("mfa_req", out _).Should().BeFalse();
        payload.TryGetProperty("amr", out _).Should().BeFalse();
        payload.TryGetProperty("auth_time", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_ApplicationToken_ClaimsNoAuthentication_EvenWithKnownMethods()
    {
        Resolves(new TokenClaims([], [], []));

        var response = await SignIn(AuthenticationMethods.Password, applicationId: Guid.NewGuid());

        var payload = DecodePayload(response.Token!.AccessToken);
        payload.TryGetProperty("amr", out _).Should().BeFalse("an application token claims no authentication it cannot show");
        payload.TryGetProperty("auth_time", out _).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_OrgPermOnly_UnderEnforcement_IsUntouched()
    {
        Resolves(PlainClaims);
        var policy = TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: false);

        var response = await SignIn(AuthenticationMethods.Password, policy);

        response.User!.MfaRequirement.Should().Be("none");
        response.User.Roles.Should().Equal("user");
        DecodePayload(response.Token!.AccessToken).TryGetProperty("mfa_req", out _).Should().BeFalse();
    }

    /// <summary>
    /// Trap 1 of the notes: with the switch off — as shipped — an administrator and
    /// an ordinary account get exactly today's claims plus amr and auth_time, and the
    /// user info adds mfaRequirement "none". Nothing withheld, no mfa_req.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAsync_SwitchOff_ClaimsSnapshot_IsTodayPlusAmrAndAuthTime(bool admin)
    {
        var resolved = admin ? AdminClaims : PlainClaims;
        Resolves(resolved);
        var policy = TestHelpers.CreatePlatformMfaPolicy(enforce: false, hasEnabledFactor: false);

        var response = await SignIn(AuthenticationMethods.Password, policy);
        var payload = DecodePayload(response.Token!.AccessToken);

        // Today's token for the same user, minted the way the builder minted it
        // before S08 (no authentication recorded).
        var today = DecodePayload(_service.GenerateAccessToken(
            _user, resolved.Permissions, resolved.RoleCodes, AccessTokenAuthentication.Unrecorded,
            Guid.NewGuid(), resolved.OrganizationPermissions));

        payload.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            today.EnumerateObject().Select(p => p.Name).Concat(["amr", "auth_time"]));
        foreach (var name in new[] { "permissions", "roles", "org_perm", "sub", "email", "name" })
        {
            payload.TryGetProperty(name, out var value).Should().Be(today.TryGetProperty(name, out var expected), name);
            if (expected.ValueKind != JsonValueKind.Undefined)
            {
                value.GetRawText().Should().Be(expected.GetRawText(), name);
            }
        }

        payload.TryGetProperty("mfa_req", out _).Should().BeFalse();
        response.User!.MfaRequirement.Should().Be("none");
        response.User.Permissions.Should().Equal(resolved.Permissions);
        response.User.Roles.Should().Equal(resolved.RoleCodes);
    }
}
