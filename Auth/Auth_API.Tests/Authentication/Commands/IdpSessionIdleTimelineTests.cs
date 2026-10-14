using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Authorize;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-154 across handlers: authorize, end-session and the refresh share one IdP
/// session table (<see cref="InMemoryIdpSessionRepository"/>, which changes rows as
/// their SQL does) and one clock the tests walk. A browser session idles out after
/// 30 minutes without use; an application's accepted refresh is a use (owner
/// decision D-154-2 (b)); and a refresh never refuses because the IdP session died.
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
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly List<AuthorizationCode> _issuedCodes = [];

    private readonly Auth.Domain.Entities.Application _application;
    private readonly User _user = TestHelpers.CreateUser();
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

        // Every rotation makes its replacement findable by its hash, as the table does.
        _refreshTokens.Setup(r => r.HasLiveTokenInSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _refreshTokens.Setup(r => r.TryRotateAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity _, RefreshTokenEntity replacement, CancellationToken _) =>
                _refreshTokens.Setup(r => r.GetByTokenHashAsync(replacement.TokenHash, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(replacement))
            .ReturnsAsync(true);
    }

    private IdpSessionReader Reader() => new(
        _table, _keys.Object, TestHelpers.CreateOptions(_settings), _clock, NullLogger<IdpSessionReader>.Instance);

    /// <summary>A browser session (no "Stay signed in") signed in now.</summary>
    private Guid SignIn()
    {
        var id = Guid.NewGuid();
        _table.Seed(new InMemoryIdpSessionRepository.Row(
            id, _user.Id, "hash:" + IdpToken, _clock.UtcNow, _clock.UtcNow.AddHours(10),
            RevokedAt: null, IsPersistent: false, LastActivityAt: null));
        return id;
    }

    /// <summary>The refresh token "first", born from <paramref name="idpSessionId"/>.</summary>
    private void IssueRefreshToken(Guid idpSessionId, bool forTheApplication)
    {
        var first = TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:first", sessionId: Guid.NewGuid(),
            applicationId: forTheApplication ? _application.Id : null,
            scope: forTheApplication ? "openid" : null,
            expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId);
        _refreshTokens.Setup(r => r.GetByTokenHashAsync("hash:first", It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
    }

    private Task<ErrorOr<AuthorizeResult>> Authorize() =>
        new AuthorizeCommandHandler(
            _applications.Object, _access.Object, Reader(), _codes.Object,
            new Mock<IAuthorizationRequestRepository>(MockBehavior.Strict).Object,
            _users.Object, _jwt.Object, _keys.Object,
            new Auth.Infrastructure.Authentication.StepUpTicketService(_keys.Object),
            new Mock<IOrganizationRepository>().Object,
            new OrganizationCreatorRoleCheck(new Mock<IRoleRepository>().Object, new Mock<IPermissionRepository>().Object),
            TestHelpers.CreateOptions(_settings), _clock, NullLogger<AuthorizeCommandHandler>.Instance)
        .Handle(
            new AuthorizeCommand("code", ClientId, RedirectUri, Challenge, "S256", "xyz", IdpToken,
                "https://auth.example.com/authorize", "127.0.0.1"),
            CancellationToken.None);

    private Task<ErrorOr<TokenResponse>> Refresh(string token) =>
        new RefreshTokenCommandHandler(
            _users.Object, _refreshTokens.Object, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(),
            _applications.Object, _access.Object, _jwt.Object, _keys.Object,
            new Mock<IUserSessionRepository>().Object, new Mock<ICredentialRevocationService>().Object,
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
}
