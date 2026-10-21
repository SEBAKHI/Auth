using System.Security.Cryptography;
using System.Text;
using System.Web;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.ServerSignOut;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Authentication.ClientCredentials;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Auth_API.Tests.Authentication.ConfidentialClients;

/// <summary>
/// OI-183 end to end inside the process: a person signs in from a browser, a server-side
/// application redeems the code with its sign-in key, and later signs the person out from its
/// server. Every step is the real one: the real <see cref="LoginResponseBuilder"/>, the real
/// <see cref="AuthorizeCommandHandler"/>, <see cref="ExchangeAuthorizationCodeCommandHandler"/> and
/// <see cref="RefreshTokenCommandHandler"/>, the real <see cref="ClientAuthenticator"/> with real ES256
/// assertions, the real <see cref="CredentialRevocationService"/> and the real IdP session reader.
/// Only the tables are in memory.
/// </summary>
public sealed class ServerSignOutTests : IDisposable
{
    private const string ServerSideUri = "https://crm.example.com/bff/callback";
    private const string Verifier = "vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv";
    private const string BrowserAddress = "198.51.100.23";
    private const string OtherBrowserAddress = "192.0.2.44";
    private const string ServerAddress = "203.0.113.7";
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0 Safari/537.36";

    private readonly MachineCredentials _signInKey = new();
    private readonly MachineCredentials _machineKey = new();
    private readonly MachineCredentials _otherApplicationsKey = new();
    private readonly Auth.Domain.Entities.Application _application = TestHelpers.CreateApplication(code: MachineCredentials.ClientId);
    private readonly Guid _otherApplicationId = Guid.NewGuid();
    private readonly User _user = TestHelpers.CreateUser(email: "person@example.com");

    private readonly InMemoryAuthorizationCodes _codes = new();
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly Dictionary<string, IdpSession> _idpSessions = [];
    private readonly List<UserSession> _sessions = [];
    private readonly HashSet<(Guid, string)> _spentJtis = [];
    private readonly List<UserSignedOutByApplicationEvent> _signOuts = [];

    private readonly Mock<IIdpSessionRepository> _idpSessionRepository = new();
    private readonly Mock<IUserSessionRepository> _sessionRepository = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly Mock<IClientAssertionJtiStore> _jtis = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly FakeCredentialHasher _hashes = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IPublisher> _publisher = new();

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

    public ServerSignOutTests()
    {
        _application.LoadConfidentialRedirectUris([ServerSideUri]);
        _applications.Setup(r => r.GetByCodeAsync(MachineCredentials.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _hashes.MapAll(value => "hash:" + value);

        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => Credential(kid));
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
        // The bulk revocations as the SQL applies them, so a test that a session stays can fail.
        _idpSessionRepository.Setup(r => r.RevokeAllForUserExceptAsync(It.IsAny<Guid>(), It.IsAny<CredentialHashCandidates?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, string? exceptHash, CancellationToken _) =>
            {
                var revoked = _idpSessions.Values
                    .Where(s => s.UserId == userId && !s.IsRevoked && s.TokenHash != exceptHash).ToList();
                revoked.ForEach(s => s.Revoke());
                return revoked.Count;
            });
        _idpSessionRepository.Setup(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback((Guid userId, CancellationToken _) =>
                _idpSessions.Values.Where(s => s.UserId == userId && !s.IsRevoked).ToList().ForEach(s => s.Revoke()))
            .Returns(Task.CompletedTask);

        _sessionRepository.Setup(r => r.CreateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback((UserSession session, CancellationToken _) => _sessions.Add(session))
            .ReturnsAsync((UserSession session, CancellationToken _) => session);

        _publisher.Setup(p => p.Publish(It.IsAny<UserSignedOutByApplicationEvent>(), It.IsAny<CancellationToken>()))
            .Callback((UserSignedOutByApplicationEvent signOut, CancellationToken _) => _signOuts.Add(signOut))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _signInKey.Dispose();
        _machineKey.Dispose();
        _otherApplicationsKey.Dispose();
    }

    private MachineKeyCredential? Credential(string kid)
    {
        if (kid == _signInKey.Kid)
        {
            return new MachineKeyCredential(Key(_signInKey, _application.Id, MachineKeyPurpose.SignIn), MachineCredentials.ClientId);
        }

        if (kid == _machineKey.Kid)
        {
            return new MachineKeyCredential(Key(_machineKey, _application.Id, MachineKeyPurpose.Machine), MachineCredentials.ClientId);
        }

        return kid == _otherApplicationsKey.Kid
            ? new MachineKeyCredential(Key(_otherApplicationsKey, _otherApplicationId, MachineKeyPurpose.SignIn), "other")
            : null;
    }

    private static ApplicationMachineKey Key(MachineCredentials credentials, Guid applicationId, MachineKeyPurpose purpose) => new(
        Guid.NewGuid(), applicationId, credentials.Kid, null, MachineClients.SigningAlgorithm,
        credentials.PublicKey.PublicKeyJwk, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), null, null, null, null, purpose);

    private string OtherApplicationsAssertion() => _otherApplicationsKey.Assertion(d =>
    {
        d.Issuer = "other";
        d.Claims["sub"] = "other";
    });

    // --- the real components ---

    private IIdpSessionReader Reader() => TestIdpSessions.Reader(_idpSessionRepository, _hashes, _idpSettings);

    private ClientAuthenticator Authenticator() => new(
        _keys.Object, _jtis.Object, new ClientAssertionVerifier(TimeProvider.System),
        TestHelpers.CreateOptions(_jwtSettings), NullLogger<ClientAuthenticator>.Instance);

    private CredentialRevocationService Revocation() => new(
        _sessionRepository.Object, _tokens, _idpSessionRepository.Object, _blacklist.Object, _hashes,
        TestHelpers.CreateOptions(_jwtSettings), Reader(), TestHelpers.CreateOptions(_idpSettings),
        new Mock<IPublisher>().Object, TimeProvider.System, NullLogger<CredentialRevocationService>.Instance);

    private LoginResponseBuilder Builder() => new(
        _claims.Object, TestHelpers.CreatePlatformMfaPolicy(), _jwt.Object, _hashes, _tokens, new Mock<IAccountLockout>().Object,
        new Mock<ILoginAttemptRepository>().Object, _sessionRepository.Object, _idpSessionRepository.Object,
        new Mock<IUserKnownDeviceRepository>().Object, new Mock<IGeoIpLookup>().Object,
        new Mock<ICredentialRevocationService>().Object, new Mock<IPublisher>().Object,
        TestHelpers.CreateOptions(_jwtSettings), TestHelpers.CreateOptions(_idpSettings),
        TestHelpers.CreateOptions(new NotificationSettings()), TestHelpers.CreateOptions(new SessionSettings()),
        TimeProvider.System, TestHelpers.BrowserSignIn(), NullLogger<LoginResponseBuilder>.Instance);

    private AuthorizeCommandHandler AuthorizeHandler() => new(
        _applications.Object, _access.Object, Reader(), _codes,
        new Mock<IAuthorizationRequestRepository>(MockBehavior.Strict).Object, _users.Object, _jwt.Object, _hashes,
        new StepUpTicketService(_hashes), new Mock<IOrganizationRepository>().Object,
        new Auth.Application.Common.OrganizationCreatorRoleCheck(
            new Mock<IRoleRepository>().Object, new Mock<IPermissionRepository>().Object),
        TestHelpers.CreateOptions(_idpSettings), TimeProvider.System, NullLogger<AuthorizeCommandHandler>.Instance);

    private ServerSignOutCommandHandler SignOutHandler(
        IRefreshTokenRepository? tokens = null, ICredentialRevocationService? revocation = null) => new(
        Authenticator(), tokens ?? _tokens, _hashes, revocation ?? Revocation(), _publisher.Object,
        TestHelpers.CreateOptions(_jwtSettings), TimeProvider.System, NullLogger<ServerSignOutCommandHandler>.Instance);

    // --- the person's steps ---

    /// <summary>The person signs in on the sign-in page; the browser keeps the IdP cookie.</summary>
    private async Task<string> SignInInteractively(string address = BrowserAddress)
    {
        var signedIn = await Builder().BuildAsync(
            _user, address, Chrome, "device", AuthenticationMethods.Password, passwordProven: true, CancellationToken.None);
        signedIn.IsError.Should().BeFalse();
        return signedIn.Value.IdpSessionToken!;
    }

    private Task<ErrorOr<AuthorizeResult>> Authorize(string idpToken) => AuthorizeHandler().Handle(
        new AuthorizeCommand("code", MachineCredentials.ClientId, ServerSideUri, Challenge(Verifier), "S256", "state",
            idpToken, "https://auth.example.com/api/v1/auth/authorize", BrowserAddress, Scope: "openid", UserAgent: Chrome),
        CancellationToken.None);

    /// <summary>The browser comes back with a code, and the server redeems it with its key.</summary>
    private async Task<string> SignInToTheApplication(string idpToken)
    {
        var authorized = await Authorize(idpToken);
        authorized.IsError.Should().BeFalse(authorized.IsError ? authorized.FirstError.Code : null);
        var code = HttpUtility.ParseQueryString(new Uri(authorized.Value.RedirectUrl).Query)["code"]!;

        var exchanged = await new ExchangeAuthorizationCodeCommandHandler(
                _codes, _applications.Object, _access.Object, _users.Object, _idpSessionRepository.Object, _hashes, Builder(),
                new Mock<ICredentialRevocationService>().Object, Authenticator(),
                NullLogger<ExchangeAuthorizationCodeCommandHandler>.Instance)
            .Handle(new ExchangeAuthorizationCodeCommand(
                    code, ServerSideUri, MachineCredentials.ClientId, Verifier, ServerAddress, "crm-server/2.1", null,
                    ClientAssertionType: ClientAuthenticator.JwtBearerAssertionType, ClientAssertion: _signInKey.Assertion()),
                CancellationToken.None);
        exchanged.IsError.Should().BeFalse(exchanged.IsError ? exchanged.FirstError.Code : null);
        return exchanged.Value.RefreshToken;
    }

    private Task<ErrorOr<ServerSignOutResult>> SignOut(
        string refreshToken, string? assertion, string? clientId = MachineCredentials.ClientId, ServerSignOutCommandHandler? handler = null) =>
        (handler ?? SignOutHandler()).Handle(
            new ServerSignOutCommand(
                refreshToken, clientId,
                assertion is null ? null : ClientAuthenticator.JwtBearerAssertionType, assertion, ServerAddress),
            CancellationToken.None);

    private Task<ErrorOr<TokenResponse>> Refresh(string refreshToken) =>
        new RefreshTokenCommandHandler(
                _users.Object, _tokens, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(),
                _applications.Object, _access.Object, _jwt.Object, _hashes,
                _sessionRepository.Object, Revocation(), Reader(), Authenticator(), new Mock<IPublisher>().Object,
                TestHelpers.CreateOptions(_jwtSettings), TestHelpers.CreateOptions(_idpSettings),
                NullLogger<RefreshTokenCommandHandler>.Instance)
            .Handle(new RefreshTokenCommand(refreshToken, ServerAddress, "crm-server/2.1",
                    ClientAssertionType: ClientAuthenticator.JwtBearerAssertionType, ClientAssertion: _signInKey.Assertion()),
                CancellationToken.None);

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private IdpSession IdpSessionOf(string idpToken) => _idpSessions["hash:" + idpToken];

    private void NothingEnded(string idpToken, string refreshToken)
    {
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse("the browser stays signed in");
        _tokens.ByHash("hash:" + refreshToken).IsRevoked.Should().BeFalse("the application session stays");
        _sessionRepository.Verify(r => r.TerminateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _signOuts.Should().BeEmpty();
    }

    private static void ShouldBe<T>(ErrorOr<T> result, Error expected)
    {
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(expected.Code);
    }

    // ===== TA1 (i): the application's key and its bound token end both sessions =====

    [Fact]
    public async Task TA1_TheServerWithItsKeyAndItsBoundToken_EndsTheLinkedIdpSession_AndTheNextAuthorizeAsksForThePassword()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var applicationToken = _tokens.ByHash("hash:" + refreshToken);
        applicationToken.IdpSessionId.Should().Be(IdpSessionOf(idpToken).Id, "the token is linked to the browser's IdP session (F5)");
        applicationToken.RequiresClientAuthentication.Should().BeTrue();

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : null);
        result.Value.SsoSessionEnded.Should().BeTrue();
        IdpSessionOf(idpToken).IsRevoked.Should().BeTrue();

        // The application's session at the server: its refresh token is spent, the session row
        // ended, and the access tokens already out are refused from now on.
        var stored = _tokens.ByHash("hash:" + refreshToken);
        stored.IsRevoked.Should().BeTrue();
        stored.ReasonRevoked.Should().Be(TokenRevocationReasons.SignedOutByApplication);
        _sessionRepository.Verify(r => r.TerminateAsync(applicationToken.SessionId!.Value,
            TokenRevocationReasons.SignedOutByApplication, It.IsAny<CancellationToken>()), Times.Once);
        _blacklist.Verify(b => b.BlacklistSession(applicationToken.SessionId!.Value.ToString(), It.IsAny<DateTime>()), Times.Once);
        (await Refresh(refreshToken)).FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);

        var again = await Authorize(idpToken);
        again.IsError.Should().BeFalse();
        again.Value.IsLoginRedirect.Should().BeTrue("the browser's next sign-in asks for the password");
        again.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/login");
    }

    [Fact]
    public async Task TA1_TheSignOut_IsAuditedOnce_WithTheApplicationAndTheSession()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var sessionId = _tokens.ByHash("hash:" + refreshToken).SessionId;

        await SignOut(refreshToken, _signInKey.Assertion());

        var signOut = _signOuts.Should().ContainSingle().Subject;
        signOut.UserId.Should().Be(_user.Id);
        signOut.ApplicationId.Should().Be(_application.Id);
        signOut.SessionId.Should().Be(sessionId);
        signOut.SingleSignOnEnded.Should().BeTrue();
        signOut.TriggeredBy.Should().Be(_user.Id);
        signOut.EventId.Should().NotBeEmpty();
    }

    // ===== TA2 (ii): nothing ends without the proof, and the answers leak nothing =====

    [Fact]
    public async Task TA2_WithoutAnAssertion_IsInvalidClient_AndTheTokenIsNeverLookedUp()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var untouched = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);

        ShouldBe(await SignOut(refreshToken, assertion: null, handler: SignOutHandler(untouched.Object)), AuthErrors.InvalidClient);

        NothingEnded(idpToken, refreshToken);
    }

    [Theory]
    [InlineData("machine key")]
    [InlineData("junk")]
    [InlineData("addressed to the token endpoint")]
    [InlineData("forged signature")]
    public async Task TA2_AnAssertionThatDoesNotAuthenticateASignInKey_IsInvalidClient_AndTheTokenIsNeverLookedUp(string kind)
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var assertion = kind switch
        {
            "machine key" => _machineKey.Assertion(),
            "junk" => "not-a-jwt",
            // The sign-in key’s kid, signed by another key: the signature is what refuses it.
            "forged signature" => _signInKey.Assertion(d => d.SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(_otherApplicationsKey.PrivateKey) { KeyId = _signInKey.Kid }, SecurityAlgorithms.EcdsaSha256)),
            _ => _signInKey.Assertion(d => d.Audience = MachineCredentials.TokenEndpoint),
        };
        var untouched = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);

        ShouldBe(await SignOut(refreshToken, assertion, handler: SignOutHandler(untouched.Object)), AuthErrors.InvalidClient);

        NothingEnded(idpToken, refreshToken);
    }

    [Fact]
    public async Task TA2_AnotherApplicationsKey_AnUnboundToken_AndAnUnknownToken_GetOneAndTheSameAnswer()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:public", sessionId: Guid.NewGuid(), applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: IdpSessionOf(idpToken).Id,
            requiresClientAuthentication: false));

        var anotherApplications = await SignOut(refreshToken, OtherApplicationsAssertion(), clientId: null);
        var unbound = await SignOut("public", _signInKey.Assertion());
        var unknown = await SignOut("no-such-token", _signInKey.Assertion());

        ShouldBe(anotherApplications, AuthErrors.InvalidToken);
        ShouldBe(unbound, AuthErrors.InvalidToken);
        ShouldBe(unknown, AuthErrors.InvalidToken);
        new[] { anotherApplications, unbound, unknown }.Select(r => (r.FirstError.Code, r.FirstError.Type, r.FirstError.Description))
            .Distinct().Should().ContainSingle("an application learns nothing about a token that is not its own");
        _spentJtis.Should().Contain(spent => spent.Item1 == _otherApplicationId,
            "the other key authenticated: what refused it is the token's binding, not the signature");

        NothingEnded(idpToken, refreshToken);
        _tokens.ByHash("hash:public").IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task TA2_AClientIdNamingAnotherApplication_WithAValidKey_IsInvalidClient_AndTheTokenIsNeverLookedUp()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var untouched = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);

        ShouldBe(await SignOut(refreshToken, _signInKey.Assertion(), clientId: "other", handler: SignOutHandler(untouched.Object)),
            AuthErrors.InvalidClient);

        NothingEnded(idpToken, refreshToken);
    }

    // ===== TA3 (iii): a repeat changes nothing =====

    [Fact]
    public async Task TA3_ARepeatedSignOut_EndsNothing_AndWritesNothing()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        (await SignOut(refreshToken, _signInKey.Assertion())).Value.SsoSessionEnded.Should().BeTrue();
        _idpSessionRepository.Invocations.Clear();
        _sessionRepository.Invocations.Clear();

        var again = await SignOut(refreshToken, _signInKey.Assertion());

        again.IsError.Should().BeFalse("a second tab signing out at once is no error");
        again.Value.SsoSessionEnded.Should().BeFalse(
            "this call ended nothing: the end-session page finds no session in the browser and sends it straight back");
        _idpSessionRepository.Verify(r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()), Times.Never);
        _sessionRepository.Verify(r => r.TerminateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _signOuts.Should().ContainSingle("only the call that ended something is audited");
    }

    [Fact]
    public async Task TA3_AReplayedAssertion_IsInvalidClient()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var assertion = _signInKey.Assertion();
        (await SignOut(refreshToken, assertion)).IsError.Should().BeFalse();

        ShouldBe(await SignOut(refreshToken, assertion), AuthErrors.InvalidClient);
    }

    // ===== F1 (REVIEW 82): true says what this call did, never what the browser holds =====

    [Fact]
    public async Task F1_TrueSaysNothingOfASecondSessionInTheSameBrowser_SignedInWithoutTheCookie()
    {
        // A browser-session cookie is dropped when the browser closes, while the application's
        // own renewals keep the linked session S1 alive on the server. The person reopens the
        // browser and signs in again: no cookie is presented, so nothing relinks, and S2 is a
        // session no token of the application names. The call ends S1 and answers true; S2 lives.
        // So the application must still send the browser through the end-session endpoint.
        var first = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(first);
        var second = await SignInInteractively();

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : null);
        result.Value.SsoSessionEnded.Should().BeTrue("this call ended the linked session S1");
        IdpSessionOf(first).IsRevoked.Should().BeTrue();
        IdpSessionOf(second).IsRevoked.Should().BeFalse("true says nothing of a session no token names");
        (await Authorize(second)).Value.IsLoginRedirect.Should().BeFalse(
            "the browser holding S2 is still signed in: only the end-session page, reading its cookie, finds that out");
    }

    // ===== TA4 (iv): the person's other browser and other sessions stay =====

    [Fact]
    public async Task TA4_TheSamePersonsOtherBrowser_StaysSignedIn_AndItsApplicationSessionStays()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var otherBrowser = await SignInInteractively(OtherBrowserAddress);
        var otherRefreshToken = await SignInToTheApplication(otherBrowser);

        (await SignOut(refreshToken, _signInKey.Assertion())).Value.SsoSessionEnded.Should().BeTrue();

        IdpSessionOf(idpToken).IsRevoked.Should().BeTrue();
        IdpSessionOf(otherBrowser).IsRevoked.Should().BeFalse("owner decision D2 (a): this browser only");
        _tokens.ByHash("hash:" + otherRefreshToken).IsRevoked.Should().BeFalse();
        (await Authorize(otherBrowser)).Value.IsLoginRedirect.Should().BeFalse("the other browser still signs in without a password");
    }

    // ===== TA6 (E1): a token with no link =====

    [Fact]
    public async Task TA6_AnUnlinkedBoundToken_EndsTheApplicationSessionAlone_AndSaysTheSingleSignOnIsNotOver()
    {
        var idpToken = await SignInInteractively();
        var sessionId = Guid.NewGuid();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:unlinked", sessionId: sessionId, applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: null, requiresClientAuthentication: true));

        var result = await SignOut("unlinked", _signInKey.Assertion());

        result.IsError.Should().BeFalse("never an error the person sees: the application falls back to the end-session page");
        result.Value.SsoSessionEnded.Should().BeFalse();
        _tokens.ByHash("hash:unlinked").IsRevoked.Should().BeTrue();
        _sessionRepository.Verify(r => r.TerminateAsync(sessionId, TokenRevocationReasons.SignedOutByApplication, It.IsAny<CancellationToken>()), Times.Once);
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse("which IdP session the token came from is unknown, so none is ended");
        _signOuts.Should().ContainSingle().Which.SingleSignOnEnded.Should().BeFalse();
    }

    // ===== TA7 (E3): a sign-out racing the server's own renewal =====

    [Fact]
    public async Task TA7_ATokenRotatedWithinTheGrace_ActsOnItsLiveReplacement()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var renewed = await Refresh(refreshToken);
        renewed.IsError.Should().BeFalse(renewed.IsError ? renewed.FirstError.Code : null);

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeTrue();
        _tokens.ByHash("hash:" + renewed.Value.RefreshToken).IsRevoked.Should().BeTrue("the session's current token is the one ended");
        IdpSessionOf(idpToken).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task TA7_ATokenRotatedLongAgo_EndsNothing_AndRaisesNoReuseCascade()
    {
        var idpToken = await SignInInteractively();
        var idpSessionId = IdpSessionOf(idpToken).Id;
        var sessionId = Guid.NewGuid();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:current", sessionId: sessionId, applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId, requiresClientAuthentication: true));
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:stale", sessionId: sessionId, applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId,
            revokedAt: DateTime.UtcNow.AddMinutes(-10), reasonRevoked: TokenRevocationReasons.Rotated,
            replacedByTokenHash: "hash:current", requiresClientAuthentication: true));
        var revocation = new Mock<ICredentialRevocationService>(MockBehavior.Strict);

        var result = await SignOut("stale", _signInKey.Assertion(), handler: SignOutHandler(revocation: revocation.Object));

        result.IsError.Should().BeFalse();
        result.Value.SsoSessionEnded.Should().BeFalse("the browser is still signed in, so the application falls back to the page");
        _tokens.ByHash("hash:current").IsRevoked.Should().BeFalse("a stale token ends nothing");
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse();
        revocation.Invocations.Should().BeEmpty("no revocation of any kind, and no cascade over the person's sessions");
        _signOuts.Should().BeEmpty();
    }

    // ===== TA8 (E2, E5): already over =====

    [Fact]
    public async Task TA8_ALinkedSessionThatDied_ThenANewSignInInTheSameBrowser_AnswersFalse_AndTheNewSessionLives()
    {
        // The linked session is revoked (a sign-out elsewhere, or a read that found it idle); the
        // application’s session lives on, since a revoked session’s cookie is no longer looked up;
        // the person then signs in again in the same browser, and that new session is not relinked.
        // "Ended" here would land the browser on the application’s page while it is still signed
        // in. An idle session no read has ended yet is another case: with single logout on, the
        // application’s chain moves to the new session (see SingleLogout_TheSamePersonSignsInAgain…).
        var first = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(first);
        IdpSessionOf(first).Revoke();
        var second = await SignInInteractively();

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.IsError.Should().BeFalse();
        result.Value.SsoSessionEnded.Should().BeFalse("the browser’s newer session is the end-session page’s to ask about");
        IdpSessionOf(second).IsRevoked.Should().BeFalse();
        _tokens.ByHash("hash:" + refreshToken).IsRevoked.Should().BeTrue("the application session still ends");
        (await Authorize(second)).Value.IsLoginRedirect.Should().BeFalse("the browser is still signed in, as the page will show");
    }

    [Fact]
    public async Task TA8_ALinkedIdpSessionAlreadyOver_AnswersFalse_AndTheApplicationSessionStillEnds()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        IdpSessionOf(idpToken).Revoke(); // signed out elsewhere: the confirmation page, or another device's "sign out everywhere"
        _idpSessionRepository.Invocations.Clear();

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeFalse("this call ended no IdP session, so it cannot vouch for the browser");
        _tokens.ByHash("hash:" + refreshToken).IsRevoked.Should().BeTrue();
        _idpSessionRepository.Verify(r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()), Times.Never,
            "a session already revoked is not written again");
    }

    [Fact]
    public async Task TA8_AnExpiredToken_EndsNothing_AndAnswersFalse()
    {
        var idpToken = await SignInInteractively();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:expired", sessionId: Guid.NewGuid(), applicationId: _application.Id,
            scope: "openid", createdAt: DateTime.UtcNow.AddDays(-8), expiresAt: DateTime.UtcNow.AddDays(-1),
            idpSessionId: IdpSessionOf(idpToken).Id, requiresClientAuthentication: true));

        var result = await SignOut("expired", _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeFalse();
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse();
        _signOuts.Should().BeEmpty();
    }

    [Fact]
    public async Task TA7_WithRotationOff_ARotatedTokensReplacementIsNotFollowed()
    {
        _jwtSettings.RotateRefreshTokens = false;
        var idpToken = await SignInInteractively();
        var (stale, current) = SeedRotatedPair(IdpSessionOf(idpToken).Id, rotatedAgo: TimeSpan.FromSeconds(5), currentBound: true);

        var result = await SignOut(stale, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeFalse();
        _tokens.ByHash("hash:" + current).IsRevoked.Should().BeFalse();
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task TA7_AReplacementThatIsNoLongerBound_IsNotActedOn()
    {
        var idpToken = await SignInInteractively();
        var (stale, current) = SeedRotatedPair(IdpSessionOf(idpToken).Id, rotatedAgo: TimeSpan.FromSeconds(5), currentBound: false);

        var result = await SignOut(stale, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeFalse();
        _tokens.ByHash("hash:" + current).IsRevoked.Should().BeFalse("only a chain bound to the key is the application's to end");
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task TA7_AReplacementInAnotherSession_IsNotActedOn()
    {
        // Rotation keeps the session, so a replacement pointer naming another session's token is
        // no chain of this one: nothing of that other session is ended.
        var idpToken = await SignInInteractively();
        var idpSessionId = IdpSessionOf(idpToken).Id;
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:elsewhere", sessionId: Guid.NewGuid(), applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId, requiresClientAuthentication: true));
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:stale", sessionId: Guid.NewGuid(), applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId,
            revokedAt: DateTime.UtcNow.AddSeconds(-5), reasonRevoked: TokenRevocationReasons.Rotated,
            replacedByTokenHash: "hash:elsewhere", requiresClientAuthentication: true));

        var result = await SignOut("stale", _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeFalse();
        _tokens.ByHash("hash:elsewhere").IsRevoked.Should().BeFalse("another session's token is not this chain");
        IdpSessionOf(idpToken).IsRevoked.Should().BeFalse();
    }

    private (string Stale, string Current) SeedRotatedPair(Guid idpSessionId, TimeSpan rotatedAgo, bool currentBound)
    {
        var sessionId = Guid.NewGuid();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:current", sessionId: sessionId, applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId,
            requiresClientAuthentication: currentBound));
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:stale", sessionId: sessionId, applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId,
            revokedAt: DateTime.UtcNow - rotatedAgo, reasonRevoked: TokenRevocationReasons.Rotated,
            replacedByTokenHash: "hash:current", requiresClientAuthentication: true));
        return ("stale", "current");
    }

    // ===== a token issued without a session =====

    [Fact]
    public async Task ASessionlessBoundToken_IsRevokedItself_AndItsIdpSessionEnds()
    {
        var idpToken = await SignInInteractively();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:legacy", sessionId: null, applicationId: _application.Id,
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: IdpSessionOf(idpToken).Id,
            requiresClientAuthentication: true));

        var result = await SignOut("legacy", _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeTrue();
        var stored = _tokens.ByHash("hash:legacy");
        stored.IsRevoked.Should().BeTrue();
        stored.ReasonRevoked.Should().Be(TokenRevocationReasons.SignedOutByApplication);
        _sessionRepository.Verify(r => r.TerminateAsync(It.IsAny<Guid>(), TokenRevocationReasons.SignedOutByApplication, It.IsAny<CancellationToken>()), Times.Never);
        _signOuts.Should().ContainSingle().Which.SessionId.Should().BeNull();
    }

    // ===== the request's shape =====

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TheValidator_RefusesAMissingRefreshToken_WithItsOwnCode(string token)
    {
        var result = new ServerSignOutCommandValidator().Validate(
            new ServerSignOutCommand(token, null, ClientAuthenticator.JwtBearerAssertionType, "assertion", ServerAddress));

        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(AuthErrors.RefreshTokenRequired.Code);
        new ServerSignOutCommandValidator().Validate(
            new ServerSignOutCommand("a-token", null, null, null, ServerAddress)).IsValid.Should().BeTrue(
            "a missing assertion is the handler's to refuse, as a failed client authentication");
    }

    // ===== the writes outlive the caller =====

    [Fact]
    public async Task OnceAuthenticated_TheWritesAndThePublish_RunWithoutTheCallersCancellation()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var revocation = new Mock<ICredentialRevocationService>();
        using var caller = new CancellationTokenSource();

        await SignOutHandler(revocation: revocation.Object).Handle(
            new ServerSignOutCommand(refreshToken, null, ClientAuthenticator.JwtBearerAssertionType, _signInKey.Assertion(), ServerAddress),
            caller.Token);

        revocation.Verify(r => r.RevokeIdpSessionByIdAsync(IdpSessionOf(idpToken).Id, CancellationToken.None), Times.Once);
        revocation.Verify(r => r.TerminateSessionAsync(It.IsAny<Guid>(), _user.Id, TokenRevocationReasons.SignedOutByApplication, CancellationToken.None), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<UserSignedOutByApplicationEvent>(), CancellationToken.None), Times.Once);
    }

    // ===== OI-155 T3: single logout from a server-side application's sign-out =====

    private Guid SeedAnotherApplicationsChain(string idpToken, string name)
    {
        var sessionId = Guid.NewGuid();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:" + name, sessionId: sessionId, applicationId: Guid.NewGuid(),
            scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: IdpSessionOf(idpToken).Id));
        return sessionId;
    }

    [Fact]
    public async Task SingleLogout_TheServersSignOut_EndsAnotherApplicationsSessionBornInTheSameBrowser()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        var other = SeedAnotherApplicationsChain(idpToken, "other-app");

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeTrue("the answer is OI-183's, unchanged");
        _tokens.ByHash("hash:other-app").IsRevoked.Should().BeTrue();
        _tokens.ByHash("hash:other-app").ReasonRevoked.Should().Be(TokenRevocationReasons.SingleLogout);
        _sessionRepository.Verify(r => r.TerminateAsync(other, TokenRevocationReasons.SingleLogout, It.IsAny<CancellationToken>()), Times.Once);
        _blacklist.Verify(b => b.BlacklistSession(other.ToString(), It.IsAny<DateTime>()), Times.Once);
        _tokens.ByHash("hash:" + refreshToken).ReasonRevoked.Should().Be(TokenRevocationReasons.SignedOutByApplication,
            "the application that signed out keeps this endpoint's reason, and is not counted by the cascade");
    }

    [Fact]
    public async Task SingleLogout_TheServersSignOut_OnALinkedSessionAlreadyOver_StillEndsTheOthers()
    {
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        SeedAnotherApplicationsChain(idpToken, "other-app");
        IdpSessionOf(idpToken).Revoke();

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeFalse("this call ended no IdP session, as in OI-183");
        _tokens.ByHash("hash:other-app").IsRevoked.Should().BeTrue("the application names its browser's session, live or not");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleLogout_TheSamePersonSignsInAgainAfterAnIdleSession_ThenTheServersSignOut(bool applicationSessionsEndWithSsoSession)
    {
        // REVIEW 84 F8. The browser's session went idle with no read ending it yet; the same
        // person signs in again in that browser.
        // Off (D-155-1 (a); TC-7): the application's chain moves to the new session (an
        // application's chain does not end with an idle one), so the server's sign-out ends the
        // browser's CURRENT session and answers true; with single logout off, nothing moved and
        // the answer was false while the browser stayed signed in.
        // On (the tree model, D-155-9; TC-9): the chain does not move from the dead session, so
        // the server's sign-out ends the application's own session and answers false: the
        // browser's new SSO session was never this application's.
        _idpSettings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var first = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(first);
        var idle = IdpSessionOf(first);
        _idpSessions["hash:" + first] = new IdpSession(
            idle.Id, idle.UserId, idle.TokenHash, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(9),
            revokedAt: null, ipAddress: null, deviceInfo: null, isPersistent: false,
            lastActivityAt: DateTime.UtcNow.AddMinutes(-31));
        var second = await SignInInteractively();

        await Revocation().ReplaceIdpSessionAsync(first, _user.Id, IdpSessionOf(second).Id, CancellationToken.None);
        var result = await SignOut(refreshToken, _signInKey.Assertion());

        if (applicationSessionsEndWithSsoSession)
        {
            _tokens.ByHash("hash:" + refreshToken).IdpSessionId.Should().Be(idle.Id, "nothing moves from a dead session");
            result.Value.SsoSessionEnded.Should().BeFalse("the session the chain was born from had already ended");
            IdpSessionOf(second).IsRevoked.Should().BeFalse("the person's new sign-in is not this application's");
        }
        else
        {
            _tokens.ByHash("hash:" + refreshToken).IdpSessionId.Should().Be(IdpSessionOf(second).Id, "the chain moved with the person");
            result.Value.SsoSessionEnded.Should().BeTrue("the browser's current SSO session is the one this call ended");
            IdpSessionOf(second).IsRevoked.Should().BeTrue();
        }
    }

    [Fact]
    public async Task SingleLogout_SwitchedOff_TheServersSignOut_LeavesTheOtherApplicationAlone()
    {
        _idpSettings.SingleLogoutEnabled = false;
        var idpToken = await SignInInteractively();
        var refreshToken = await SignInToTheApplication(idpToken);
        SeedAnotherApplicationsChain(idpToken, "other-app");

        var result = await SignOut(refreshToken, _signInKey.Assertion());

        result.Value.SsoSessionEnded.Should().BeTrue();
        _tokens.ByHash("hash:other-app").IsRevoked.Should().BeFalse("off, as before OI-155");
    }
}
