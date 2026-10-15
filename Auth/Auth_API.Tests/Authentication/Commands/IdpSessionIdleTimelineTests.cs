using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.SystemSettings.ResetSystemSettings;
using Auth.Application.Features.SystemSettings.UpdateSystemSettings;
using Auth.Application.Interfaces;
using Auth.Application.SystemSettings;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Administration;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-154 across handlers: authorize, end-session and the refresh share one IdP
/// session table (<see cref="InMemoryIdpSessionRepository"/>, which changes rows as
/// their SQL does) and one clock the tests walk. A browser session idles out after
/// 30 minutes without use; an application's accepted refresh is a use (owner
/// decision D-154-2 (b)); an application's refresh is never refused because the
/// IdP session died, and a platform app's own refresh is (D-74-1 (c)). The refresh
/// tokens live in one table too (<see cref="InMemoryRefreshTokenRepository"/>),
/// revoked and re-pointed by the real <see cref="CredentialRevocationService"/>.
/// </summary>
public class IdpSessionIdleTimelineTests
{
    private const string ClientId = "CRM";
    private const string RedirectUri = "https://crm.example.com/callback";
    private const string IdpToken = "sso-token";
    private static readonly string Challenge = new('a', 43);

    private readonly AdjustableTimeProvider _clock = new();
    private readonly InMemoryIdpSessionRepository _table = new();
    private readonly IdentityProviderSettings _settings = new()
    {
        AccountsBaseUrl = "https://accounts.example.com",
        AuthorizationRequestHandlesEnabled = false
    };

    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IAuthorizationCodeRepository> _codes = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly List<AuthorizationCode> _issuedCodes = [];

    private readonly Auth.Domain.Entities.Application _application;
    private readonly User _user = TestHelpers.CreateUser();
    private readonly User _nextPerson = TestHelpers.CreateUser();
    private int _issued;

    public IdpSessionIdleTimelineTests()
    {
        _clock.Advance(TimeSpan.Zero);
        _keys.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns((string value) => "hash:" + value);

        _application = TestHelpers.CreateApplication(code: ClientId);
        _application.LoadRedirectUris([RedirectUri]);
        _applications.Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _users.Setup(r => r.GetByIdAsync(_nextPerson.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_nextPerson);
        _codes.Setup(r => r.CreateAsync(It.IsAny<AuthorizationCode>(), It.IsAny<CancellationToken>()))
            .Callback((AuthorizationCode code, CancellationToken _) => _issuedCodes.Add(code))
            .ReturnsAsync((AuthorizationCode code, CancellationToken _) => code);

        _claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns(() => "issued-" + ++_issued);
        _jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");
    }

    private IdpSessionReader Reader() => new(
        _table, _keys.Object, TestHelpers.CreateOptions(_settings), _clock, NullLogger<IdpSessionReader>.Instance);

    private CredentialRevocationService Revocation() => new(
        new Mock<IUserSessionRepository>().Object, _tokens, _table, new Mock<ITokenBlacklistService>().Object,
        _keys.Object, TestHelpers.CreateOptions(new JwtSettings()), Reader(), NullLogger<CredentialRevocationService>.Instance);

    /// <summary>
    /// An IdP session signed in now, by default a browser session (no "Stay signed
    /// in") of the first person, in the browser holding <see cref="IdpToken"/>.
    /// </summary>
    private Guid SignIn(User? person = null, string cookie = IdpToken, bool staySignedIn = false)
    {
        var id = Guid.NewGuid();
        _table.Seed(new InMemoryIdpSessionRepository.Row(
            id, (person ?? _user).Id, "hash:" + cookie, _clock.UtcNow,
            _clock.UtcNow.Add(staySignedIn ? TimeSpan.FromDays(7) : TimeSpan.FromHours(10)),
            RevokedAt: null, IsPersistent: staySignedIn, LastActivityAt: null));
        return id;
    }

    /// <summary>
    /// The refresh token <paramref name="name"/> of the first person, born from
    /// <paramref name="idpSessionId"/>: an application's, or the platform app's own
    /// (console or accounts).
    /// </summary>
    private void IssueRefreshToken(Guid? idpSessionId, bool forTheApplication, string name = "first")
    {
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:" + name, sessionId: Guid.NewGuid(),
            applicationId: forTheApplication ? _application.Id : null,
            scope: forTheApplication ? "openid" : null,
            expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId));
    }

    /// <summary>What the sign-in filter does when a sign-in replaces this browser's session.</summary>
    private Task ReplaceTheBrowsersSession(User person, Guid newIdpSessionId) =>
        Revocation().ReplaceIdpSessionAsync(IdpToken, person.Id, newIdpSessionId, CancellationToken.None);

    private Task<ErrorOr<AuthorizeResult>> Authorize(string cookie = IdpToken) =>
        new AuthorizeCommandHandler(
            _applications.Object, _access.Object, Reader(), _codes.Object,
            new Mock<IAuthorizationRequestRepository>(MockBehavior.Strict).Object,
            _users.Object, _jwt.Object, _keys.Object,
            new Auth.Infrastructure.Authentication.StepUpTicketService(_keys.Object),
            new Mock<IOrganizationRepository>().Object,
            new OrganizationCreatorRoleCheck(new Mock<IRoleRepository>().Object, new Mock<IPermissionRepository>().Object),
            TestHelpers.CreateOptions(_settings), _clock, NullLogger<AuthorizeCommandHandler>.Instance)
        .Handle(
            new AuthorizeCommand("code", ClientId, RedirectUri, Challenge, "S256", "xyz", cookie,
                "https://auth.example.com/authorize", "127.0.0.1"),
            CancellationToken.None);

    private Task<ErrorOr<TokenResponse>> Refresh(string token) =>
        new RefreshTokenCommandHandler(
            _users.Object, _tokens, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(),
            _applications.Object, _access.Object, _jwt.Object, _keys.Object,
            new Mock<IUserSessionRepository>().Object, Revocation(),
            Reader(), new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings
            {
                AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7, RotateRefreshTokens = true
            }),
            NullLogger<RefreshTokenCommandHandler>.Instance)
        .Handle(new RefreshTokenCommand(token, "127.0.0.1", "agent"), CancellationToken.None);

    private Task<ErrorOr<EndSessionResult>> EndSession() =>
        new EndSessionCommandHandler(
            _applications.Object, Reader(), TestHelpers.CreateOptions(_settings),
            NullLogger<EndSessionCommandHandler>.Instance)
        .Handle(new EndSessionCommand(ClientId, "xyz", IdpToken), CancellationToken.None);

    private void AuthorizeShouldIssueACode(ErrorOr<AuthorizeResult> result, string because)
    {
        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{RedirectUri}?code=", because);
    }

    private void AuthorizeShouldAskToSignIn(ErrorOr<AuthorizeResult> result, string because)
    {
        result.IsError.Should().BeFalse();
        result.Value.IsLoginRedirect.Should().BeTrue(because);
        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/login", because);
        _issuedCodes.Should().BeEmpty(because);
    }

    // --- T3 and T4 at authorize and end-session --------------------------------

    [Fact]
    public async Task Authorize_UsedEveryTwentyMinutes_KeepsSigningIn()
    {
        SignIn();

        for (var step = 1; step <= 6; step++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            AuthorizeShouldIssueACode(await Authorize(), $"a sign-in through authorize {step * 20} minutes in");
        }
    }

    [Fact]
    public async Task Authorize_AfterThirtyOneMinutesUnused_AsksForTheSignIn_AndTheLimitRaisedLaterDoesNotReviveIt()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        AuthorizeShouldAskToSignIn(await Authorize(), "the browser session went idle");
        _table[id].RevokedAt.Should().NotBeNull();

        _settings.IdpBrowserSessionIdleMinutes = 120;
        AuthorizeShouldAskToSignIn(await Authorize(), "revoked when found idle, it stays ended");
        _table.Revocations[id].Should().Be(1);
    }

    [Fact]
    public async Task Authorize_ThatRefuses_RecordsNoUse()
    {
        // A use is a sign-in into something: access_denied signs the person into
        // nothing, so a page that keeps sending the browser here keeps no session
        // alive this way.
        var id = SignIn();
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = await Authorize();

        result.Value.RedirectUrl.Should().Contain("error=access_denied");
        _table[id].LastActivityAt.Should().BeNull();
    }

    [Fact]
    public async Task Authorize_ThatIssuesACode_RecordsTheUse()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(10));

        AuthorizeShouldIssueACode(await Authorize(), "a live session");

        _table[id].LastActivityAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task EndSession_OfASessionGoneIdle_IsTheNoSessionAnswer()
    {
        SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        var result = await EndSession();

        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/signed-out");
    }

    [Fact]
    public async Task EndSession_OfALiveSession_RecordsNoUse()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(10));

        var result = await EndSession();

        result.Value.RedirectUrl.Should().StartWith("https://accounts.example.com/logout");
        _table[id].LastActivityAt.Should().BeNull("asking whether to sign out is not a use of the session");
    }

    // --- T14: an application's refresh counts as use ---------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshesEveryTwentyMinutesForTwoHours_ThenAnAuthorize_IssuesACodeWithNoPassword(bool forTheApplication)
    {
        // With the application's token, and with the platform app's own (console or
        // accounts), which counts the same way.
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication);

        var token = "first";
        for (var step = 1; step <= 6; step++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            var refreshed = await Refresh(token);
            refreshed.IsError.Should().BeFalse($"refresh {step}");
            token = refreshed.Value.RefreshToken;
        }

        AuthorizeShouldIssueACode(await Authorize(),
            "two hours without an authorize, but the person was at work in the application the whole time");
    }

    [Fact]
    public async Task RefreshesThatStopForThirtyOneMinutes_ThenAnAuthorize_AsksForTheSignIn()
    {
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication: true);

        var token = "first";
        for (var step = 1; step <= 3; step++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            token = (await Refresh(token)).Value.RefreshToken;
        }

        _clock.Advance(TimeSpan.FromMinutes(31));

        AuthorizeShouldAskToSignIn(await Authorize(), "31 minutes with neither a sign-in nor a refresh");
    }

    // --- T15 and T17: a refresh never revives, and is never refused ------------

    [Fact]
    public async Task ARefreshAfterTheSessionWentIdle_IsAccepted_ButDoesNotReviveTheSession()
    {
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication: true);
        _clock.Advance(TimeSpan.FromMinutes(31));

        var refreshed = await Refresh("first");

        refreshed.IsError.Should().BeFalse("the IdP session's death does not refuse the application's refresh (OI-155)");
        _table[id].LastActivityAt.Should().BeNull("the activity write refuses a session that died idle");
        AuthorizeShouldAskToSignIn(await Authorize(), "the refresh did not bring the session back");
    }

    [Fact]
    public async Task ARefreshAfterTheSessionWasRevoked_IsStillAccepted()
    {
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication: true);
        _table.Seed(_table[id] with { RevokedAt = _clock.UtcNow });

        var refreshed = await Refresh("first");

        refreshed.IsError.Should().BeFalse();
        _table[id].LastActivityAt.Should().BeNull();
    }

    // --- T16: the code carries the session the request was answered from -------

    [Fact]
    public async Task TheCode_CarriesTheIdpSessionTheRequestWasAnsweredFrom()
    {
        var id = SignIn();

        AuthorizeShouldIssueACode(await Authorize(), "a live session");

        _issuedCodes.Should().ContainSingle().Which.IdpSessionId.Should().Be(id);
    }

    // --- D-74-1 (c): a platform app's own sign-in ends with its IdP session ----
    // (ii), refreshes every 20 minutes for 2 hours, is the theory above with the
    // platform app's own token.

    private static void RefreshShouldBeRefused(ErrorOr<TokenResponse> result, string because)
    {
        result.IsError.Should().BeTrue(because);
        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code, because);
    }

    [Fact]
    public async Task PlatformSignIn_ThirtyOneMinutesUnused_ThenItsRefresh_IsRefused_AndTheChainEnds()
    {
        // (i) Signed in on the accounts page without «Stay signed in», then left.
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication: false);
        _clock.Advance(TimeSpan.FromMinutes(31));

        RefreshShouldBeRefused(await Refresh("first"), "the browser session it was born with went idle");

        _tokens.ByHash("hash:first").ReasonRevoked.Should().Be(TokenRevocationReasons.IdpSessionEnded);
        _table[id].RevokedAt.Should().NotBeNull("found idle, the IdP session itself is ended too");
        RefreshShouldBeRefused(await Refresh("first"),
            "presented again, the ended chain answers «session ended», never the theft cascade");
    }

    [Fact]
    public async Task PlatformSignIn_WithStaySignedIn_ThirtyOneMinutesUnused_ThenItsRefresh_IsAccepted()
    {
        // (iii)
        var id = SignIn(staySignedIn: true);
        IssueRefreshToken(id, forTheApplication: false);
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Refresh("first")).IsError.Should().BeFalse("a «Stay signed in» session has no idle limit");
    }

    [Fact]
    public async Task AnotherPersonSigningInOnTheSameBrowser_EndsTheFirstPersonsPlatformSignIn()
    {
        // (iv) The first person works in the console; the next one signs in on the
        // accounts page of the same browser, which still carries the first cookie.
        var first = SignIn();
        IssueRefreshToken(first, forTheApplication: false, name: "console");
        _clock.Advance(TimeSpan.FromMinutes(5));

        var next = SignIn(_nextPerson, cookie: "sso-token-next");
        await ReplaceTheBrowsersSession(_nextPerson, next);

        RefreshShouldBeRefused(await Refresh("console"), "the first person's IdP session was replaced by another person's");
        _tokens.ByHash("hash:console").IdpSessionId.Should().Be(first, "another person's sign-in moves nothing");
    }

    [Fact]
    public async Task ThePersonSigningInAgain_MovesTheirChainsOntoTheNewSession()
    {
        // (v) A re-sign-in in the same browser (prompt=login, or an application's
        // re-authentication policy) replaces the session both chains were born with.
        var first = SignIn();
        IssueRefreshToken(first, forTheApplication: true, name: "rp1");
        IssueRefreshToken(first, forTheApplication: false, name: "console");
        _clock.Advance(TimeSpan.FromMinutes(5));

        var second = SignIn(cookie: "sso-token-2");
        await ReplaceTheBrowsersSession(_user, second);

        var token = "rp1";
        for (var step = 1; step <= 2; step++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            var refreshed = await Refresh(token);
            refreshed.IsError.Should().BeFalse($"the application's refresh {step * 20} minutes after the re-sign-in");
            token = refreshed.Value.RefreshToken;
        }

        AuthorizeShouldIssueACode(await Authorize("sso-token-2"),
            "40 minutes after the re-sign-in, the application's refreshes counted as the new session's use");
        (await Refresh("console")).IsError.Should().BeFalse("the person's own console sign-in moved onto the new session");
    }

    [Fact]
    public async Task ThePersonSigningInAgain_AfterTheOldSessionWentIdleUnread_MovesNothing()
    {
        // Review F15: the old session died idle and nobody read it since, so it is
        // not revoked yet. Its chains are already refused; the new sign-in must not
        // bring them back by moving them onto the new session.
        var first = SignIn();
        IssueRefreshToken(first, forTheApplication: false, name: "console");
        _clock.Advance(TimeSpan.FromMinutes(31));

        var second = SignIn(cookie: "sso-token-2");
        await ReplaceTheBrowsersSession(_user, second);

        _tokens.ByHash("hash:console").IdpSessionId.Should().Be(first, "nothing moved off a dead session");
        RefreshShouldBeRefused(await Refresh("console"), "the session it was born with died idle before the new sign-in");
    }

    [Fact]
    public async Task APlatformTokenFromBeforeTheLink_RefreshesAsBefore()
    {
        // (vi) NULL link: issued before the column existed.
        IssueRefreshToken(idpSessionId: null, forTheApplication: false);
        _clock.Advance(TimeSpan.FromHours(11));

        (await Refresh("first")).IsError.Should().BeFalse("no link, nothing to end it with");
    }

    [Fact]
    public async Task APlatformTokenWhoseSessionWasPurged_IsRefused()
    {
        // (vi) Dangling link. The session is minted before its token is written, so
        // a link names a row that existed; no row means the retention sweep deleted
        // it, which it does only to a session that ended days before. A refresh
        // lifetime above the retention period makes this reachable.
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication: false);
        _table.Purge(id);

        RefreshShouldBeRefused(await Refresh("first"), "a purged session ended long ago");
    }

    [Fact]
    public async Task AnApplicationTokenWhoseSessionWasPurged_IsStillAccepted()
    {
        var id = SignIn();
        IssueRefreshToken(id, forTheApplication: true);
        _table.Purge(id);

        (await Refresh("first")).IsError.Should().BeFalse("an application's refresh is never refused for its IdP session (OI-155)");
    }

    // --- D-74-2 (a): a console change of the idle limit revives nothing --------

    private IdpSessionIdleLimitChange IdleLimitChange() => new(
        _table, TestHelpers.CreateOptions(_settings), _clock, NullLogger<IdpSessionIdleLimitChange>.Instance);

    /// <summary>
    /// Saves the idle limit through the console's write path, then applies it as
    /// the configuration reload does.
    /// </summary>
    private async Task SaveIdleLimit(int minutes)
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"));
        var rows = new Mock<ISystemSettingsRepository>();
        rows.Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 2, 3, 4, 5, 6, 7, 8], 1));

        var saved = await new UpdateSystemSettingsCommandHandler(
                rows.Object, new Mock<IUserRepository>().Object, configuration,
                SystemSettingsTestSupport.SnapshotOf(configuration), new Mock<ISystemSettingsReloader>().Object,
                new Mock<IPublisher>().Object, IdleLimitChange(), NullLogger<UpdateSystemSettingsCommandHandler>.Instance)
            .Handle(
                new UpdateSystemSettingsCommand(
                    "IdentityProvider",
                    SystemSettingsTestSupport.Json($$"""{"IdpBrowserSessionIdleMinutes":{{minutes}}}"""),
                    null,
                    Guid.NewGuid()),
                CancellationToken.None);

        saved.IsError.Should().BeFalse();
        _settings.IdpBrowserSessionIdleMinutes = minutes;
    }

    private Task<IdpSession?> UseTheBrowsersSession() =>
        Reader().FindLiveAsync(IdpToken, IdpSessionReadMode.Use, CancellationToken.None);

    [Theory]
    [InlineData(120)]
    [InlineData(0)]
    public async Task NeverReadAfterGoingIdle_RaisingTheLimitOrSwitchingItOff_DoesNotBringItBack(int newLimit)
    {
        // The person left; nothing read the session since it went idle, so its row
        // is still unrevoked when the administrator changes the limit.
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        await SaveIdleLimit(newLimit);

        (await UseTheBrowsersSession()).Should().BeNull("it was idle under the limit in force when the change was saved");
        _table[id].RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RaisingTheLimit_KeepsASessionInUse()
    {
        SignIn();
        _clock.Advance(TimeSpan.FromMinutes(20));

        await SaveIdleLimit(120);
        _clock.Advance(TimeSpan.FromMinutes(60));

        (await UseTheBrowsersSession()).Should().NotBeNull("80 minutes unused is within the new limit, and it was not idle before");
    }

    [Fact]
    public async Task LoweringTheLimit_RunsNoStatement()
    {
        SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        await SaveIdleLimit(15);

        _table.BulkIdleEndings.Should().Be(0, "a lower limit ends idle sessions at their next read anyway");
    }

    [Fact]
    public async Task ResettingToAFileValueThatSwitchesItOff_EndsTheIdleSessionsFirst()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));
        var file = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"),
            ("IdentityProvider:IdpBrowserSessionIdleMinutes", "0"));
        var rows = new Mock<ISystemSettingsRepository>();
        rows.Setup(r => r.GetAsync("IdentityProvider", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("IdentityProvider", """{"IdpBrowserSessionIdleMinutes":30}"""));

        var reset = await new ResetSystemSettingsCommandHandler(
                rows.Object, file, SystemSettingsTestSupport.SnapshotOf(file), new Mock<ISystemSettingsReloader>().Object,
                new Mock<IPublisher>().Object, IdleLimitChange(), NullLogger<ResetSystemSettingsCommandHandler>.Instance)
            .Handle(new ResetSystemSettingsCommand("IdentityProvider", Guid.NewGuid()), CancellationToken.None);
        reset.IsError.Should().BeFalse();
        _settings.IdpBrowserSessionIdleMinutes = 0;

        (await UseTheBrowsersSession()).Should().BeNull("the reset to the file's 0 ended it first");
        _table[id].RevokedAt.Should().NotBeNull();
    }

    // --- D-74-3 (a): only a browser's sign-in is tied to its IAM session -------
    // Through the real builder, over the same two tables: the sign-in is the one a
    // platform app or a server makes with POST /auth/login.

    private LoginResponseBuilder Builder(bool fromABrowserPage)
    {
        var sessions = new Mock<IUserSessionRepository>();
        sessions.Setup(r => r.GetActiveSessionPressureAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSessionPressure(0, null));

        return new LoginResponseBuilder(
            _claims.Object, TestHelpers.CreatePlatformMfaPolicy(), _jwt.Object, _keys.Object, _tokens,
            new Mock<IAccountLockout>().Object, new Mock<ILoginAttemptRepository>().Object, sessions.Object, _table,
            new Mock<IUserKnownDeviceRepository>().Object, new Mock<IGeoIpLookup>().Object, Revocation(),
            new Mock<IPublisher>().Object,
            TestHelpers.CreateOptions(new JwtSettings { AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7 }),
            TestHelpers.CreateOptions(_settings),
            TestHelpers.CreateOptions(new NotificationSettings { NewDeviceAlertEnabled = false }),
            TestHelpers.CreateOptions(new SessionSettings()),
            _clock,
            TestHelpers.BrowserSignIn(fromABrowserPage),
            NullLogger<LoginResponseBuilder>.Instance);
    }

    /// <summary>An unticked sign-in through the builder; returns its refresh token.</summary>
    private async Task<string> SignInThroughTheBuilder(bool fromABrowserPage)
    {
        var signedIn = await Builder(fromABrowserPage).BuildAsync(
            _user, "127.0.0.1", "agent", null, AuthenticationMethods.Password, passwordProven: true, CancellationToken.None);
        signedIn.IsError.Should().BeFalse();
        return signedIn.Value.Token!.RefreshToken;
    }

    [Fact]
    public async Task ASignInWithoutAnOrigin_ThirtyOneMinutesUnused_StillRefreshes_AndItsTokenIsUnlinked()
    {
        // A server or the SDK: no Origin. Its refresh behaves as it always did.
        var refreshToken = await SignInThroughTheBuilder(fromABrowserPage: false);
        _tokens.ByHash("hash:" + refreshToken).IdpSessionId.Should().BeNull("nothing ties it to the IAM session");
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Refresh(refreshToken)).IsError.Should().BeFalse("its refresh behaves as it always did");
    }

    [Fact]
    public async Task ABrowserSignIn_Unticked_ThirtyOneMinutesUnused_IsRefused()
    {
        var refreshToken = await SignInThroughTheBuilder(fromABrowserPage: true);
        _tokens.ByHash("hash:" + refreshToken).IdpSessionId.Should().NotBeNull();
        _clock.Advance(TimeSpan.FromMinutes(31));

        RefreshShouldBeRefused(await Refresh(refreshToken), "the browser session it was born with went idle");
    }

    // --- OI-165, D-165-1 (b): a change of StaySignedInMode touches no session ---
    // Minted through the real builder under the default, then the setting changes:
    // the row keeps what it was given, and so does every later read of it.

    /// <summary>A sign-in through the builder now; returns its IdP session's id and cookie value.</summary>
    private async Task<(Guid Id, string Cookie)> MintThroughTheBuilder(bool staySignedIn)
    {
        var signedIn = await Builder(fromABrowserPage: true).BuildAsync(
            _user, "127.0.0.1", "agent", null, AuthenticationMethods.Password, passwordProven: true,
            CancellationToken.None, persistent: staySignedIn);
        signedIn.IsError.Should().BeFalse();
        return (signedIn.Value.IdpSessionId!.Value, signedIn.Value.IdpSessionToken!);
    }

    [Fact]
    public async Task ASessionTickedBefore_TheModeTurnsToAlwaysBrowserSession_KeepsItsRow_AndSignsInSixDaysLater()
    {
        var (id, cookie) = await MintThroughTheBuilder(staySignedIn: true);
        var before = _table[id];
        before.IsPersistent.Should().BeTrue();

        // The administrator switches the mode through the console's save path.
        var saved = await UpdateHandler(_table).Handle(
            new UpdateSystemSettingsCommand(
                "IdentityProvider", SystemSettingsTestSupport.Json("""{"StaySignedInMode":"AlwaysBrowserSession"}"""), null, Guid.NewGuid()),
            CancellationToken.None);
        saved.IsError.Should().BeFalse();
        _settings.StaySignedInMode = nameof(StaySignedInPolicy.AlwaysBrowserSession);
        _clock.Advance(TimeSpan.FromDays(6));

        AuthorizeShouldIssueACode(await Authorize(cookie), "a persistent session has no idle limit and lasts its seven days");
        // Read after the save and the authorize: only the use the authorize recorded differs.
        _table[id].Should().Be(before with { LastActivityAt = _clock.UtcNow },
            "the setting is read only when a session is minted");
        _table[id].RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task ABrowserSessionFromBefore_TheModeTurnsToAlwaysStaySignedIn_StillIdlesOut()
    {
        var (id, cookie) = await MintThroughTheBuilder(staySignedIn: false);

        _settings.StaySignedInMode = nameof(StaySignedInPolicy.AlwaysStaySignedIn);
        _clock.Advance(TimeSpan.FromMinutes(31));

        AuthorizeShouldAskToSignIn(await Authorize(cookie), "it was minted a browser session and stays one");
        _table[id].IsPersistent.Should().BeFalse();
    }

    private UpdateSystemSettingsCommandHandler UpdateHandler(IIdpSessionRepository sessions)
    {
        var configuration = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"));
        var rows = new Mock<ISystemSettingsRepository>();
        rows.Setup(r => r.UpsertAsync(It.IsAny<SystemSettingsOverride>(), It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsUpsertResult(true, [1, 2, 3, 4, 5, 6, 7, 8], 1));
        return new UpdateSystemSettingsCommandHandler(
            rows.Object, new Mock<IUserRepository>().Object, configuration,
            SystemSettingsTestSupport.SnapshotOf(configuration), new Mock<ISystemSettingsReloader>().Object,
            new Mock<IPublisher>().Object,
            new IdpSessionIdleLimitChange(
                sessions, TestHelpers.CreateOptions(_settings), _clock, NullLogger<IdpSessionIdleLimitChange>.Instance),
            NullLogger<UpdateSystemSettingsCommandHandler>.Instance);
    }

    private ResetSystemSettingsCommandHandler ResetHandler(IIdpSessionRepository sessions)
    {
        var file = SystemSettingsTestSupport.BuildConfiguration(
            ("IdentityProvider:AccountsBaseUrl", "https://accounts.example.com"));
        var rows = new Mock<ISystemSettingsRepository>();
        rows.Setup(r => r.GetAsync("IdentityProvider", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SystemSettingsTestSupport.ExistingRow("IdentityProvider", """{"StaySignedInMode":"AlwaysBrowserSession"}"""));
        return new ResetSystemSettingsCommandHandler(
            rows.Object, file, SystemSettingsTestSupport.SnapshotOf(file), new Mock<ISystemSettingsReloader>().Object,
            new Mock<IPublisher>().Object,
            new IdpSessionIdleLimitChange(
                sessions, TestHelpers.CreateOptions(_settings), _clock, NullLogger<IdpSessionIdleLimitChange>.Instance),
            NullLogger<ResetSystemSettingsCommandHandler>.Instance);
    }

    [Theory]
    [InlineData("AlwaysBrowserSession")]
    [InlineData("AlwaysStaySignedIn")]
    [InlineData("PersonChooses")]
    public async Task SavingOnlyTheMode_CallsNoSessionStatement(string mode)
    {
        // A strict repository: any call at all, a conversion included, throws.
        var sessions = new Mock<IIdpSessionRepository>(MockBehavior.Strict);

        var saved = await UpdateHandler(sessions.Object).Handle(
            new UpdateSystemSettingsCommand(
                "IdentityProvider", SystemSettingsTestSupport.Json($$"""{"StaySignedInMode":"{{mode}}"}"""), null, Guid.NewGuid()),
            CancellationToken.None);

        saved.IsError.Should().BeFalse();
        sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SavingOrResettingTheMode_LeavesEverySessionRowAsItWas()
    {
        var (ticked, _) = await MintThroughTheBuilder(staySignedIn: true);
        var (unticked, _) = await MintThroughTheBuilder(staySignedIn: false);
        _clock.Advance(TimeSpan.FromMinutes(31));
        var before = new[] { _table[ticked], _table[unticked] };

        var saved = await UpdateHandler(_table).Handle(
            new UpdateSystemSettingsCommand(
                "IdentityProvider", SystemSettingsTestSupport.Json("""{"StaySignedInMode":"AlwaysBrowserSession"}"""), null, Guid.NewGuid()),
            CancellationToken.None);
        _settings.StaySignedInMode = nameof(StaySignedInPolicy.AlwaysBrowserSession);
        var reset = await ResetHandler(_table).Handle(
            new ResetSystemSettingsCommand("IdentityProvider", Guid.NewGuid()), CancellationToken.None);
        _settings.StaySignedInMode = nameof(StaySignedInPolicy.PersonChooses);

        saved.IsError.Should().BeFalse();
        reset.IsError.Should().BeFalse();
        new[] { _table[ticked], _table[unticked] }.Should().Equal(before);
        _table.BulkIdleEndings.Should().Be(0);
        _table.Revocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ResettingTheMode_CallsNoSessionStatement()
    {
        var sessions = new Mock<IIdpSessionRepository>(MockBehavior.Strict);
        _settings.StaySignedInMode = nameof(StaySignedInPolicy.AlwaysBrowserSession);

        var reset = await ResetHandler(sessions.Object).Handle(
            new ResetSystemSettingsCommand("IdentityProvider", Guid.NewGuid()), CancellationToken.None);

        reset.IsError.Should().BeFalse();
        sessions.VerifyNoOtherCalls();
    }
}
