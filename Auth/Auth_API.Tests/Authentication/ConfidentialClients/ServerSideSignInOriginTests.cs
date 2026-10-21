using System.Security.Cryptography;
using System.Text;
using System.Web;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Authentication.ClientCredentials;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.ConfidentialClients;

/// <summary>
/// OI-178 end to end inside the process: a person signs in from a browser, the browser asks for a
/// code at authorize, and a server-side application redeems the code and renews the token from its
/// own address with its own user agent. Every step is the real one: the real
/// <see cref="LoginResponseBuilder"/> for the interactive sign-in and for the exchange, the real
/// <see cref="AuthorizeCommandHandler"/>, the real <see cref="ExchangeAuthorizationCodeCommandHandler"/>
/// and <see cref="RefreshTokenCommandHandler"/>, real ES256 assertions through the real
/// <see cref="ClientAuthenticator"/>. Only the tables are in memory, and the session row, the device
/// ledger, the sign-in history and the alerts they hold are what the person's lists and e-mails read.
/// </summary>
public sealed class ServerSideSignInOriginTests : IDisposable
{
    private const string ServerSideUri = "https://crm.example.com/bff/callback";
    private const string PublicUri = "https://crm.example.com/callback";
    private const string Verifier = "vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv";

    // The person: address A, Chrome on Windows, the device id their sign-in page sent.
    private const string BrowserAddress = "198.51.100.23";
    private const string ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";
    private const string BrowserDevice = "0f8fad5b-d9cb-469f-a165-70867728950e";

    // The application's server: address B, its own HTTP library, no device.
    private const string ServerAddress = "203.0.113.7";
    private const string ServerAgent = "crm-server/2.1 (HttpClient)";

    private readonly MachineCredentials _signInKey = new();
    private readonly Auth.Domain.Entities.Application _application = TestHelpers.CreateApplication(code: MachineCredentials.ClientId);
    private readonly User _user = TestHelpers.CreateUser(email: "person@example.com");

    // --- the tables ---
    private readonly InMemoryAuthorizationCodes _codes = new();
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly Dictionary<string, IdpSession> _idpSessions = [];
    private readonly List<UserSession> _sessions = [];
    private readonly List<LoginAttempt> _attempts = [];
    private readonly InMemoryKnownDevices _devices = new();
    private readonly List<NewDeviceSignInEvent> _alerts = [];
    private readonly HashSet<(Guid, string)> _spentJtis = [];

    private readonly Mock<IIdpSessionRepository> _idpSessionRepository = new();
    private readonly Mock<IUserSessionRepository> _sessionRepository = new();
    private readonly Mock<ILoginAttemptRepository> _attemptRepository = new();
    private readonly Mock<IAccountLockout> _lockout = new();
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly Mock<IClientAssertionJtiStore> _jtis = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeCredentialHasher _hashes = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<IGeoIpLookup> _geo = new();

    private readonly JwtSettings _jwtSettings = new()
    {
        Issuer = MachineCredentials.Issuer,
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 7,
        RotateRefreshTokens = true,
        ApplicationRefreshReplayGraceSeconds = 30,
    };
    private readonly IdentityProviderSettings _idpSettings = new()
    {
        AccountsBaseUrl = "https://accounts.example.com",
        AuthorizationRequestHandlesEnabled = false
    };
    private int _issued;

    public ServerSideSignInOriginTests()
    {
        _application.LoadRedirectUris([PublicUri]);
        _application.LoadConfidentialRedirectUris([ServerSideUri]);
        _applications.Setup(r => r.GetByCodeAsync(MachineCredentials.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _hashes.MapAll(value => "hash:" + value);

        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => kid == _signInKey.Kid
                ? new MachineKeyCredential(new ApplicationMachineKey(
                    Guid.NewGuid(), _application.Id, _signInKey.Kid, null, MachineClients.SigningAlgorithm,
                    _signInKey.PublicKey.PublicKeyJwk, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), null, null, null, null,
                    MachineKeyPurpose.SignIn), MachineCredentials.ClientId)
                : null);
        _keys.Setup(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid application, string jti, DateTime _, CancellationToken _) => _spentJtis.Add((application, jti)));

        _claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns(() => "random-" + ++_issued);
        _jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");

        _idpSessionRepository.Setup(r => r.CreateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdpSession session, CancellationToken _) => _idpSessions[session.TokenHash] = session);
        _idpSessionRepository.Setup(r => r.GetByTokenHashAsync(It.IsAny<CredentialHashCandidates>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialHashCandidates hashes, CancellationToken _) =>
                hashes.Values.Select(_idpSessions.GetValueOrDefault).FirstOrDefault(session => session is not null));
        _idpSessionRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _idpSessions.Values.SingleOrDefault(s => s.Id == id));

        _sessionRepository.Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback((UserSession session, CancellationToken _) => _sessions.Add(session))
            .ReturnsAsync((UserSession session, CancellationToken _) => session);
        _sessionRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _sessions.SingleOrDefault(s => s.Id == id));
        _sessionRepository.Setup(r => r.TouchOnRefreshAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _attemptRepository.Setup(r => r.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()))
            .Callback((LoginAttempt attempt, CancellationToken _) => _attempts.Add(attempt))
            .Returns(Task.CompletedTask);

        _publisher.Setup(p => p.Publish(It.IsAny<NewDeviceSignInEvent>(), It.IsAny<CancellationToken>()))
            .Callback((NewDeviceSignInEvent alert, CancellationToken _) => _alerts.Add(alert))
            .Returns(Task.CompletedTask);
        _geo.Setup(g => g.Resolve(It.IsAny<string?>())).Returns((string? address) => "near " + address);
    }

    public void Dispose() => _signInKey.Dispose();

    // --- the real components ---

    private LoginResponseBuilder Builder() => new(
        _claims.Object, TestHelpers.CreatePlatformMfaPolicy(), _jwt.Object, _hashes, _tokens, _lockout.Object,
        _attemptRepository.Object, _sessionRepository.Object, _idpSessionRepository.Object, _devices, _geo.Object,
        new Mock<ICredentialRevocationService>().Object, _publisher.Object,
        TestHelpers.CreateOptions(_jwtSettings), TestHelpers.CreateOptions(_idpSettings),
        TestHelpers.CreateOptions(new NotificationSettings()), TestHelpers.CreateOptions(new SessionSettings()),
        TimeProvider.System, TestHelpers.BrowserSignIn(), NullLogger<LoginResponseBuilder>.Instance);

    private ClientAuthenticator Authenticator() => new(
        _keys.Object, _jtis.Object, new ClientAssertionVerifier(TimeProvider.System),
        TestHelpers.CreateOptions(_jwtSettings), NullLogger<ClientAuthenticator>.Instance);

    private IIdpSessionReader Reader() =>
        TestIdpSessions.Reader(_idpSessionRepository, _hashes, _idpSettings);

    private AuthorizeCommandHandler AuthorizeHandler() => new(
        _applications.Object, _access.Object, Reader(), _codes,
        new Mock<IAuthorizationRequestRepository>(MockBehavior.Strict).Object, _users.Object, _jwt.Object, _hashes,
        new StepUpTicketService(_hashes), new Mock<IOrganizationRepository>().Object,
        new Auth.Application.Common.OrganizationCreatorRoleCheck(
            new Mock<IRoleRepository>().Object, new Mock<IPermissionRepository>().Object),
        TestHelpers.CreateOptions(_idpSettings), TimeProvider.System, NullLogger<AuthorizeCommandHandler>.Instance);

    /// <summary>The person signs in on the sign-in page: the browser's request, the browser's facts.</summary>
    private async Task<string> SignInInteractively(
        string address = BrowserAddress, string userAgent = ChromeOnWindows, string? deviceId = BrowserDevice)
    {
        var signedIn = await Builder().BuildAsync(
            _user, address, userAgent, deviceId, AuthenticationMethods.Password, passwordProven: true, CancellationToken.None);
        signedIn.IsError.Should().BeFalse();
        return signedIn.Value.IdpSessionToken!;
    }

    /// <summary>The browser, holding the IdP cookie, asks for a code: GET authorize.</summary>
    private async Task<string> Authorize(
        string idpToken, string redirectUri, string address = BrowserAddress, string? userAgent = ChromeOnWindows)
    {
        var answered = await AuthorizeHandler().Handle(
            new AuthorizeCommand("code", MachineCredentials.ClientId, redirectUri, Challenge(Verifier), "S256", "state",
                idpToken, "https://auth.example.com/api/v1/auth/authorize", address, Scope: "openid", UserAgent: userAgent),
            CancellationToken.None);
        answered.IsError.Should().BeFalse(answered.IsError ? answered.FirstError.Code : null);
        return HttpUtility.ParseQueryString(new Uri(answered.Value.RedirectUrl).Query)["code"]!;
    }

    /// <summary>The token request: from the server with its key, or from the browser without one.</summary>
    private async Task<OAuthTokenResponse> Exchange(
        string code, string redirectUri, bool withKey,
        string address = ServerAddress, string userAgent = ServerAgent, string? deviceId = null)
    {
        var exchanged = await new ExchangeAuthorizationCodeCommandHandler(
                _codes, _applications.Object, _access.Object, _users.Object, _idpSessionRepository.Object, _hashes, Builder(),
                new Mock<ICredentialRevocationService>().Object, Authenticator(),
                NullLogger<ExchangeAuthorizationCodeCommandHandler>.Instance)
            .Handle(new ExchangeAuthorizationCodeCommand(
                    code, redirectUri, MachineCredentials.ClientId, Verifier, address, userAgent, deviceId,
                    ClientAssertionType: withKey ? ClientAuthenticator.JwtBearerAssertionType : null,
                    ClientAssertion: withKey ? _signInKey.Assertion() : null),
                CancellationToken.None);
        exchanged.IsError.Should().BeFalse(exchanged.IsError ? exchanged.FirstError.Code : null);
        return exchanged.Value;
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private UserSession ApplicationSession() => _sessions.Single(s => s.ApplicationId == _application.Id);

    private LoginAttempt ApplicationSignIn() => _attempts.Single(a => a.ApplicationId == _application.Id);

    /// <summary>
    /// The address leg of the familiar-source check, as LoginAttemptRepository.HasSucceededFromAsync
    /// asks it: a successful sign-in of this person from this address.
    /// </summary>
    private bool IsFamiliarAddress(string address) =>
        _attempts.Any(a => a.UserId == _user.Id && a.IsSuccess && a.IpAddress == address);

    // ===== TA1: the session describes the person, not the server =====

    [Fact]
    public async Task TA1_AServerSideSignIn_RecordsTheBrowserAtAuthorize_NotTheServerThatRedeemedTheCode()
    {
        var idpToken = await SignInInteractively();
        var code = await Authorize(idpToken, ServerSideUri);

        await Exchange(code, ServerSideUri, withKey: true);

        var interactive = _sessions.Single(s => s.ApplicationId is null);
        var session = ApplicationSession();
        session.IpAddress.Should().Be(BrowserAddress);
        session.UserAgent.Should().Be(ChromeOnWindows);
        session.DeviceName.Should().Be("Chrome on Windows");
        session.DeviceId.Should().Be(BrowserDevice);
        session.DeviceHash.Should().Be(interactive.DeviceHash, "the same browser as the sign-in, by the same signature");
        session.Location.Should().Be("near " + BrowserAddress);

        var signIn = ApplicationSignIn();
        signIn.IpAddress.Should().Be(BrowserAddress, "the person's sign-in history");
        signIn.UserAgent.Should().Be(ChromeOnWindows);
        _lockout.Verify(l => l.RecordSignInAsync(_user.Id, BrowserAddress, false, It.IsAny<CancellationToken>()), Times.Once,
            "the administrator's last sign-in address");
        _lockout.Verify(l => l.RecordSignInAsync(It.IsAny<Guid>(), ServerAddress, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===== TA2, TA3: the new-device alert =====

    [Fact]
    public async Task TA2_TheSameBrowser_ThroughTheServer_IsAKnownDevice_AndRaisesNoAlertNamingTheServer()
    {
        var idpToken = await SignInInteractively();
        _devices.Rows.Should().ContainSingle("the sign-in recorded the browser");

        await Exchange(await Authorize(idpToken, ServerSideUri), ServerSideUri, withKey: true);

        _alerts.Should().BeEmpty("the person signed in from this browser moments ago");
        _devices.Rows.Should().ContainSingle("no pseudo-device for the server joins the person's list");
    }

    [Fact]
    public async Task TA3_TheSignInCookieReplayedFromAnotherBrowser_RaisesTheAlert_NamingThatBrowserAndAddress()
    {
        // Before OI-178 every sign-in through the server carried the server's user agent: after the
        // first one, any browser at all was "known" on this path, so a stolen IdP cookie used from
        // another browser to sign in to a server-side application raised nothing.
        const string FirefoxOnLinux = "Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0";
        const string OtherAddress = "192.0.2.99";
        var idpToken = await SignInInteractively();
        await Exchange(await Authorize(idpToken, ServerSideUri), ServerSideUri, withKey: true);

        await Exchange(await Authorize(idpToken, ServerSideUri, OtherAddress, FirefoxOnLinux), ServerSideUri, withKey: true);

        var alert = _alerts.Should().ContainSingle().Subject;
        alert.DeviceName.Should().Be("Firefox on Linux");
        alert.IpAddress.Should().Be(OtherAddress);
    }

    // ===== TA4, TA5, TA6: which branch applies =====

    [Fact]
    public async Task TA4_APublicRedemption_IsRecordedFromTheTokenRequest_AsBefore()
    {
        const string SpaAddress = "198.51.100.40";
        const string SpaDevice = "spa-device";
        var idpToken = await SignInInteractively();
        var code = await Authorize(idpToken, PublicUri);

        await Exchange(code, PublicUri, withKey: false, SpaAddress, ChromeOnWindows, SpaDevice);

        var session = ApplicationSession();
        session.IpAddress.Should().Be(SpaAddress, "a public client's token request is the browser itself");
        session.DeviceId.Should().Be(SpaDevice);
        ApplicationSignIn().IpAddress.Should().Be(SpaAddress);
    }

    [Fact]
    public async Task TA5_APublicCode_RedeemedWithTheKey_IsAServer_AndRecordsTheBrowser()
    {
        // E2, OI-171 CA5: a server that started public and now authenticates.
        var idpToken = await SignInInteractively();

        await Exchange(await Authorize(idpToken, PublicUri), PublicUri, withKey: true);

        ApplicationSession().IpAddress.Should().Be(BrowserAddress);
        ApplicationSession().UserAgent.Should().Be(ChromeOnWindows);
    }

    [Fact]
    public async Task TA6_ACodeWrittenBeforeTheColumns_IsRecordedFromTheTokenRequest_AsBefore()
    {
        // E3: minted by the previous build in the seconds around the deploy: no user agent, no
        // device. Rebuilt as the repository rebuilds such a row; a code minted now never has one.
        await SignInInteractively();
        _codes.Seed(new AuthorizationCode(
            Guid.NewGuid(), _application.Id, _user.Id, "hash:old-code", ServerSideUri, Challenge(Verifier),
            DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60), consumedAt: null, BrowserAddress,
            requiresClientAuthentication: true));

        await Exchange("old-code", ServerSideUri, withKey: true);

        ApplicationSession().IpAddress.Should().Be(ServerAddress);
        ApplicationSession().UserAgent.Should().Be(ServerAgent);
    }

    [Fact]
    public async Task TA6_LeavingTheUserAgentOutAtAuthorize_StillRecordsTheBrowsersAddress_NeverTheServers()
    {
        // NULL marks a code written before the columns, which answers from the token request.
        // Authorize must never write it, or anyone holding an IdP cookie could drop one header
        // and have the server's address recorded: no alert, and the server "familiar" again.
        const string OtherAddress = "192.0.2.77";
        var idpToken = await SignInInteractively();

        var code = await Authorize(idpToken, ServerSideUri, OtherAddress, userAgent: null);
        await Exchange(code, ServerSideUri, withKey: true);

        _codes.Stored("hash:" + code).UserAgent.Should().BeEmpty("recorded as sent: nothing");
        var session = ApplicationSession();
        session.IpAddress.Should().Be(OtherAddress);
        session.UserAgent.Should().BeNull();
        IsFamiliarAddress(ServerAddress).Should().BeFalse();
        _alerts.Should().ContainSingle("a browser that names itself nothing is not the one the person signed in from")
            .Which.IpAddress.Should().Be(OtherAddress);
    }

    // ===== TA7: the code records the request that produced it =====

    [Fact]
    public async Task TA7_TheCode_CarriesTheAuthorizeRequestsAgent_AndTheSignInsDevice()
    {
        var idpToken = await SignInInteractively();

        var code = await Authorize(idpToken, ServerSideUri);

        var stored = _codes.Stored("hash:" + code);
        stored.IpAddress.Should().Be(BrowserAddress);
        stored.UserAgent.Should().Be(ChromeOnWindows);
        stored.DeviceId.Should().Be(BrowserDevice, "copied from the IdP session: a navigation sends no device header");
    }

    [Fact]
    public void TA7_AResumedAuthorize_TakesTheResumeRequestsAgent()
    {
        // The resume is a navigation too, made by the same browser after the sign-in page.
        var stored = AuthorizationRequest.Create(
            "handle-hash", _application.Id, MachineCredentials.ClientId, "code", ServerSideUri, Challenge(Verifier), "S256", "state",
            "login", maxAge: null, "openid", createOrganization: false, TimeSpan.FromMinutes(10), DateTime.UtcNow);

        var command = AuthorizeCommandHandler.FromStored(stored, new ResumeAuthorizationCommand(
            MachineCredentials.ClientId, "handle", SkipOrganization: false, "idp", null, BrowserAddress, ChromeOnWindows));

        command.UserAgent.Should().Be(ChromeOnWindows);
        command.IpAddress.Should().Be(BrowserAddress);
    }

    // ===== TA8: a renewal changes nothing the person reads =====

    [Fact]
    public async Task TA8_TheServersRenewal_LeavesTheSessionWhereThePersonSignedIn()
    {
        const string RenewalAddress = "203.0.113.8";
        var idpToken = await SignInInteractively();
        var tokens = await Exchange(await Authorize(idpToken, ServerSideUri), ServerSideUri, withKey: true);
        var (sessions, attempts) = (_sessions.Count, _attempts.Count);
        _sessionRepository.Invocations.Clear();

        var renewed = await new RefreshTokenCommandHandler(
                _users.Object, _tokens, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(), _applications.Object, _access.Object,
                _jwt.Object, _hashes, _sessionRepository.Object, new Mock<ICredentialRevocationService>().Object, Reader(),
                Authenticator(), _publisher.Object, TestHelpers.CreateOptions(_jwtSettings),
                TestHelpers.CreateOptions(_idpSettings), NullLogger<RefreshTokenCommandHandler>.Instance)
            .Handle(new RefreshTokenCommand(tokens.RefreshToken!, RenewalAddress, ServerAgent,
                    ClientAssertionType: ClientAuthenticator.JwtBearerAssertionType, ClientAssertion: _signInKey.Assertion()),
                CancellationToken.None);

        renewed.IsError.Should().BeFalse(renewed.IsError ? renewed.FirstError.Code : null);
        _sessions.Should().HaveCount(sessions, "a renewal opens no session");
        _attempts.Should().HaveCount(attempts, "a renewal is not a sign-in in the history");
        _alerts.Should().BeEmpty();
        _sessionRepository.Verify(r => r.TouchOnRefreshAsync(
            ApplicationSession().Id, _user.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessionRepository.Verify(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()), Times.Never);
        // The read and the touch, nothing else: any other write could move the session's address.
        _sessionRepository.Verify(r => r.GetByIdAsync(ApplicationSession().Id, It.IsAny<CancellationToken>()), Times.AtMostOnce);
        _sessionRepository.VerifyNoOtherCalls();
        ApplicationSession().IpAddress.Should().Be(BrowserAddress);
        _lockout.Verify(l => l.RecordSignInAsync(It.IsAny<Guid>(), RenewalAddress, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===== TA9: the server is not a familiar place =====

    [Fact]
    public async Task TA9_AfterAServerSideSignIn_TheServersAddressIsNotAFamiliarSource_ThePersonsIs()
    {
        // The familiar-source check admits a sign-in through an automatic lock from an address the
        // person signed in from within 30 days. Recorded with the server's address, every person
        // behind that server would share it, and so would anyone else on the server's egress.
        var idpToken = await SignInInteractively(address: "198.51.100.1");

        await Exchange(await Authorize(idpToken, ServerSideUri), ServerSideUri, withKey: true);

        IsFamiliarAddress(ServerAddress).Should().BeFalse();
        IsFamiliarAddress(BrowserAddress).Should().BeTrue("the browser that asked for the code");
    }

    /// <summary>
    /// The UserKnownDevices table as the Dapper repository keeps it: one row per (user, signature),
    /// and the insert decides who discovered a device.
    /// </summary>
    private sealed class InMemoryKnownDevices : IUserKnownDeviceRepository
    {
        private readonly Dictionary<(Guid, string), UserKnownDevice> _rows = [];

        public IReadOnlyCollection<UserKnownDevice> Rows => _rows.Values;

        public Task<UserKnownDevice?> GetAsync(Guid userId, string deviceHash, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.GetValueOrDefault((userId, deviceHash)));

        public Task<bool> HasAnyAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Keys.Any(key => key.Item1 == userId));

        public Task<DateTime?> GetLastAlertAtAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Values.Where(r => r.UserId == userId).Max(r => r.LastAlertSentAt));

        public Task<IReadOnlyList<UserKnownDevice>> GetForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserKnownDevice>>(_rows.Values.Where(r => r.UserId == userId).ToList());

        public Task<UserKnownDevice?> GetByIdAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.Values.SingleOrDefault(r => r.UserId == userId && r.Id == deviceId));

        public Task<bool> DeleteAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not part of the OI-178 flows.");

        public Task<bool> AdoptLegacySignatureAsync(
            Guid userId, string legacyHash, string currentHash, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> UpsertAsync(UserKnownDevice device, CancellationToken cancellationToken)
        {
            var inserted = !_rows.ContainsKey((device.UserId, device.DeviceHash));
            _rows[(device.UserId, device.DeviceHash)] = device;
            return Task.FromResult(inserted);
        }
    }
}
