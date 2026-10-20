using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Features.Authentication.Logout;
using Auth.Application.Features.Authentication.LogoutWithIdpSessionCookie;
using Auth.Application.Features.Authentication.LogoutWithRefreshCookie;
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
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-155 PR A: an explicit end of a browser's IdP session (a sign-out there, or another person
/// signing in on it) ends every sign-in born from that session, the applications' and the
/// platform apps' own, through the real revocation service over the in-memory tables. An idle or
/// lifetime end runs no cascade (the tree model, PR C, ends those chains at their next refresh
/// instead), and the switch off restores the behaviour before OI-155.
/// PR B1: that end also reaches a session an earlier read already ended as idle.
/// </summary>
public class SingleLogoutTests
{
    private const string BrowserX = "sso-x";
    private const string BrowserY = "sso-y";

    private readonly AdjustableTimeProvider _clock = new();
    private readonly InMemoryIdpSessionRepository _idpSessions = new();
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly Mock<IUserSessionRepository> _sessionRows = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly IdentityProviderSettings _settings = new();

    private readonly Guid _person = Guid.NewGuid();
    private readonly Guid _application = Guid.NewGuid();
    private readonly Guid _otherApplication = Guid.NewGuid();

    public SingleLogoutTests()
    {
        _keys.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns((string t) => "hash:" + t);
    }

    private IdpSessionReader Reader() =>
        new(_idpSessions, _keys.Object, TestHelpers.CreateOptions(_settings), _clock, NullLogger<IdpSessionReader>.Instance);

    private CredentialRevocationService Revocation(
        IRefreshTokenRepository? tokens = null, IIdpSessionRepository? idpSessions = null) => new(
        _sessionRows.Object, tokens ?? _tokens, idpSessions ?? _idpSessions, _blacklist.Object, _keys.Object,
        TestHelpers.CreateOptions(new JwtSettings()), Reader(), TestHelpers.CreateOptions(_settings),
        _publisher.Object, _clock, NullLogger<CredentialRevocationService>.Instance);

    /// <summary>A browser session signed in now in the browser holding <paramref name="cookie"/>.</summary>
    private Guid SignIn(string cookie = BrowserX, Guid? person = null)
    {
        var id = Guid.NewGuid();
        _idpSessions.Seed(new InMemoryIdpSessionRepository.Row(
            id, person ?? _person, "hash:" + cookie, _clock.UtcNow, _clock.UtcNow.AddHours(10),
            RevokedAt: null, IsPersistent: false, LastActivityAt: _clock.UtcNow));
        return id;
    }

    /// <summary>
    /// A live refresh chain born from <paramref name="idpSessionId"/>: an application's, or a
    /// platform app's own (no application). Returns its session id (null for a session-less token).
    /// </summary>
    private Guid? Issue(
        Guid? idpSessionId, Guid? applicationId, string name, Guid? person = null, bool withSession = true)
    {
        Guid? sessionId = withSession ? Guid.NewGuid() : null;
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: person ?? _person, tokenHash: "hash:" + name, sessionId: sessionId,
            applicationId: applicationId, scope: applicationId is null ? null : "openid",
            expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: idpSessionId));
        return sessionId;
    }

    private RefreshToken Token(string name) => _tokens.ByHash("hash:" + name);

    private void ShouldHaveEnded(string name, Guid? sessionId)
    {
        Token(name).IsRevoked.Should().BeTrue($"{name} was born from the session that was ended");
        Token(name).ReasonRevoked.Should().Be(TokenRevocationReasons.SingleLogout);
        if (sessionId is { } id)
        {
            _sessionRows.Verify(r => r.TerminateAsync(id, TokenRevocationReasons.SingleLogout, It.IsAny<CancellationToken>()), Times.Once);
            _blacklist.Verify(b => b.BlacklistSession(id.ToString(), It.IsAny<DateTime>()), Times.Once,
                "its access token already out must stop now");
        }
    }

    private void ShouldBeLive(string name, string because) => Token(name).IsRevoked.Should().BeFalse(because);

    private LogoutCommandHandler LogoutHandler() => new(
        _tokens, _blacklist.Object, _keys.Object, new Mock<IJwtTokenService>().Object, _sessionRows.Object,
        Revocation(), _idpSessions, _publisher.Object, TestHelpers.CreateOptions(new JwtSettings()),
        NullLogger<LogoutCommandHandler>.Instance);

    private Task SignOutOfTheConsole(Guid consoleSession, string cookie = BrowserX) =>
        LogoutHandler().Handle(
            new LogoutCommand(_person, null, null, "198.51.100.7", SessionId: consoleSession, IdpSessionToken: cookie),
            CancellationToken.None);

    // ===== T1: the console's sign-out on this device =====

    [Fact]
    public async Task ConsoleSignOut_EndsTheApplicationSessionBornInTheSameBrowser()
    {
        var x = SignIn();
        var console = Issue(x, applicationId: null, "console")!.Value;
        var crm = Issue(x, _application, "crm");

        await SignOutOfTheConsole(console);

        ShouldHaveEnded("crm", crm);
        _idpSessions[x].RevokedAt.Should().NotBeNull("the sign-out ends the browser's single sign-on first");
    }

    // ===== T2: the end-session confirmation page =====

    [Fact]
    public async Task ConfirmEndSession_EndsEverySignInBornFromThatSession()
    {
        var x = SignIn();
        var accounts = Issue(x, applicationId: null, "accounts");
        var crm = Issue(x, _application, "crm");
        var other = Issue(x, _otherApplication, "other");

        var result = await new ConfirmEndSessionCommandHandler(Revocation(), NullLogger<ConfirmEndSessionCommandHandler>.Instance)
            .Handle(new ConfirmEndSessionCommand(BrowserX), CancellationToken.None);

        result.IsError.Should().BeFalse();
        ShouldHaveEnded("crm", crm);
        ShouldHaveEnded("other", other);
        ShouldHaveEnded("accounts", accounts);
    }

    [Fact]
    public async Task ASignOut_OnASessionThatWentIdleUnread_StillEndsTheChainsLinkedToIt()
    {
        // Idle, but no read has ended it yet: the person who signs out now means this browser,
        // and the chains still linked to it are theirs.
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));

        await Revocation().RevokeIdpSessionAsync(BrowserX, CancellationToken.None);

        ShouldHaveEnded("crm", crm);
    }

    // ===== PR B1 (D-155-4): a session a read already ended as idle is still reached =====

    /// <summary>The three sign-outs that name the browser's session by its cookie.</summary>
    public enum CookieSignOut
    {
        Console,
        RefreshCookie,
        ConfirmationPage,
    }

    private async Task SignOutBy(CookieSignOut path, string platformApp)
    {
        switch (path)
        {
            case CookieSignOut.Console:
                await SignOutOfTheConsole(Token(platformApp).SessionId!.Value);
                break;
            case CookieSignOut.RefreshCookie:
                await new LogoutWithRefreshCookieCommandHandler(
                        _tokens, _keys.Object, Revocation(), _publisher.Object,
                        NullLogger<LogoutWithRefreshCookieCommandHandler>.Instance)
                    .Handle(new LogoutWithRefreshCookieCommand(platformApp, null, BrowserX), CancellationToken.None);
                break;
            case CookieSignOut.ConfirmationPage:
                await new ConfirmEndSessionCommandHandler(Revocation(), NullLogger<ConfirmEndSessionCommandHandler>.Instance)
                    .Handle(new ConfirmEndSessionCommand(BrowserX), CancellationToken.None);
                break;
        }
    }

    /// <summary>
    /// The usual case on a shared computer: unused past the idle limit, then the next person opens
    /// an application, whose authorize step reads the session first and ends it as idle.
    /// </summary>
    private async Task<DateTime> AReadEndsItIdle(Guid idpSessionId)
    {
        _clock.Advance(TimeSpan.FromMinutes(31));
        (await Reader().IsLiveAsync(idpSessionId, CancellationToken.None)).Should().BeFalse();
        return _idpSessions[idpSessionId].RevokedAt ?? throw new InvalidOperationException("the read did not end it");
    }

    [Theory]
    // The two sign-outs of the console's own session end it first, under its own reason; the
    // confirmation page names no session, so the cascade ends the console's chain as well.
    [InlineData(CookieSignOut.Console, 2)]
    [InlineData(CookieSignOut.RefreshCookie, 2)]
    [InlineData(CookieSignOut.ConfirmationPage, 3)]
    public async Task ASignOut_OnASessionAReadAlreadyEnded_StillEndsTheApplicationSessionsBornThere(
        CookieSignOut path, int endedByTheCascade)
    {
        var x = SignIn();
        Issue(x, applicationId: null, "console");
        var crm = Issue(x, _application, "crm");
        var other = Issue(x, _otherApplication, "other");
        var endedIdle = await AReadEndsItIdle(x);

        await SignOutBy(path, "console");

        ShouldHaveEnded("crm", crm);
        ShouldHaveEnded("other", other);
        _idpSessions[x].RevokedAt.Should().Be(endedIdle, "a revoked session is not revoked again");
        _idpSessions.Revocations[x].Should().Be(1);
        _publisher.Verify(p => p.Publish(
            It.Is<SingleLogoutCompletedEvent>(e => e.UserId == _person && e.IdpSessionId == x &&
                                                   e.Trigger == SingleLogoutTriggers.SignOut && e.EndedSignInCount == endedByTheCascade),
            CancellationToken.None), Times.Once, "one audit row for the cascade");
    }

    [Fact]
    public async Task ASignOut_OnASessionWhoseRowRetentionDeleted_EndsNothingMore()
    {
        // The residual: the retention sweep deletes a revoked row after DataRetention:IdpSessionDays,
        // and the cookie then names nothing.
        var x = SignIn();
        Issue(x, _application, "crm");
        await AReadEndsItIdle(x);
        _idpSessions.Purge(x);

        await Revocation().RevokeIdpSessionAsync(BrowserX, CancellationToken.None);

        ShouldBeLive("crm", "a deleted row names no session");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===== T4: scope =====

    [Fact]
    public async Task SignOut_LeavesAnotherBrowsersSignInsAndUnlinkedChainsAlone()
    {
        var x = SignIn(BrowserX);
        var y = SignIn(BrowserY);
        var console = Issue(x, applicationId: null, "console")!.Value;
        Issue(y, _application, "crm-on-y");
        Issue(idpSessionId: null, _application, "crm-unlinked");
        var stranger = Guid.NewGuid();
        Issue(x, _application, "stranger-on-x", person: stranger);

        await SignOutOfTheConsole(console);

        ShouldBeLive("crm-on-y", "the same person's other browser keeps its sign-ins");
        ShouldBeLive("crm-unlinked", "a chain from before the link names no browser");
        ShouldBeLive("stranger-on-x", "only the IdP session's own person's chains are read");
    }

    // ===== T5: another person signs in on the browser =====

    [Fact]
    public async Task AnotherPersonSigningIn_EndsThePreviousPersonsSignInsBornThere()
    {
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        var console = Issue(x, applicationId: null, "console");
        var next = Guid.NewGuid();

        await Revocation().ReplaceIdpSessionAsync(BrowserX, next, Guid.NewGuid(), CancellationToken.None);

        ShouldHaveEnded("crm", crm);
        ShouldHaveEnded("console", console);
        _idpSessions[x].RevokedAt.Should().NotBeNull();
        _publisher.Verify(p => p.Publish(
            It.Is<SingleLogoutCompletedEvent>(e => e.UserId == _person && e.Trigger == SingleLogoutTriggers.OtherUserSignedIn &&
                                                   e.TriggeredBy == next && e.EndedSignInCount == 2),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AnotherPersonSigningIn_AfterThePreviousSessionWentIdle_StillEndsItsApplicationSignIns()
    {
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));

        await Revocation().ReplaceIdpSessionAsync(BrowserX, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        ShouldHaveEnded("crm", crm);
        _idpSessions[x].RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AnotherPersonSigningIn_AfterAReadEndedThePreviousSessionIdle_StillEndsItsSignIns()
    {
        // PR B1 (D-155-4): the next person's authorize step ended it idle just before they signed in.
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        var console = Issue(x, applicationId: null, "console");
        var endedIdle = await AReadEndsItIdle(x);
        var next = Guid.NewGuid();

        await Revocation().ReplaceIdpSessionAsync(BrowserX, next, Guid.NewGuid(), CancellationToken.None);

        ShouldHaveEnded("crm", crm);
        ShouldHaveEnded("console", console);
        _idpSessions[x].RevokedAt.Should().Be(endedIdle, "a revoked session is not revoked again");
        _publisher.Verify(p => p.Publish(
            It.Is<SingleLogoutCompletedEvent>(e => e.UserId == _person && e.Trigger == SingleLogoutTriggers.OtherUserSignedIn &&
                                                   e.TriggeredBy == next && e.EndedSignInCount == 2),
            CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSamePersonSigningInAgain_AfterAReadEndedTheirSessionIdle_MovesTheApplicationChainOnlyWhenOff(
        bool applicationSessionsEndWithSsoSession)
    {
        // PR B1: their own authorize step ended it idle first. Nothing ends for them at the
        // sign-in. Off (D-155-1 (a)): the application's chain moves, so a later sign-out in this
        // browser still reaches it. On (the tree model, D-155-9, TC-9): nothing moves from a
        // dead session; the application's chain is refused at its next refresh.
        _settings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var x = SignIn();
        Issue(x, _application, "crm");
        Issue(x, applicationId: null, "console");
        await AReadEndsItIdle(x);
        var replacement = SignIn(BrowserY);

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);

        Token("crm").IdpSessionId.Should().Be(applicationSessionsEndWithSsoSession ? x : replacement,
            applicationSessionsEndWithSsoSession
                ? "on, a new sign-in does not bring back a chain whose session died"
                : "off, an application's chain does not end with an idle session, so a later sign-out here must still reach it");
        Token("console").IdpSessionId.Should().Be(x, "a platform app's own chain ended with its idle session (OI-154)");
        ShouldBeLive("crm", "nothing ends for the same person at the sign-in");
        ShouldBeLive("console", "nothing ends for the same person; its idle session refuses its refresh");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheSamePersonSigningInAgain_EndsNothing_AndMovesTheirChainsToTheNewSession()
    {
        var x = SignIn();
        Issue(x, _application, "crm");
        Issue(x, applicationId: null, "console");
        var replacement = Guid.NewGuid();

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);

        ShouldBeLive("crm", "the same person signing in again ends nothing");
        ShouldBeLive("console", "the same person signing in again ends nothing");
        Token("crm").IdpSessionId.Should().Be(replacement);
        Token("console").IdpSessionId.Should().Be(replacement, "from a live session every chain moves, as before OI-155");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSamePersonSigningInAgain_AfterIdle_MovesTheApplicationChainOnlyWhenOff(bool applicationSessionsEndWithSsoSession)
    {
        // TC-9: idle, merely not revoked yet.
        _settings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var x = SignIn();
        Issue(x, _application, "crm");
        Issue(x, applicationId: null, "console");
        _clock.Advance(TimeSpan.FromMinutes(31));
        var replacement = Guid.NewGuid();

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);

        Token("crm").IdpSessionId.Should().Be(applicationSessionsEndWithSsoSession ? x : replacement,
            applicationSessionsEndWithSsoSession
                ? "on, a new sign-in does not bring back a chain whose session died"
                : "off, an application's chain does not end with an idle session, so a later sign-out here must still reach it");
        Token("console").IdpSessionId.Should().Be(x, "a platform app's own chain ended with its idle session (OI-154)");
        ShouldBeLive("crm", "nothing ends for the same person at the sign-in");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSamePersonSigningInAgain_ThenSigningOut_EndsTheMovedApplicationChainOnlyWhenOff(
        bool applicationSessionsEndWithSsoSession)
    {
        // Off, the moved chain is reached by the later sign-out. On, it did not move, so that
        // sign-out does not reach it; it ends at its next refresh instead (D-155-9), which
        // IdpSessionIdleTimelineTests' TC-9 drives end to end.
        _settings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));
        var replacement = SignIn(BrowserY);

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);
        await Revocation().RevokeIdpSessionAsync(BrowserY, CancellationToken.None);

        if (applicationSessionsEndWithSsoSession)
        {
            ShouldBeLive("crm", "on, it stayed on its dead session, which this sign-out does not end");
            Token("crm").IdpSessionId.Should().Be(x);
        }
        else
        {
            ShouldHaveEnded("crm", crm);
        }
    }

    [Fact]
    public async Task ASignInByAnUnknownPerson_EndsOnlyTheOldSession()
    {
        var x = SignIn();
        Issue(x, _application, "crm");

        await Revocation().ReplaceIdpSessionAsync(BrowserX, signedInUserId: null, Guid.NewGuid(), CancellationToken.None);

        _idpSessions[x].RevokedAt.Should().NotBeNull();
        ShouldBeLive("crm", "a person who cannot be told from the same one ends nothing more");
    }

    // ===== T6: an idle or lifetime end is not a sign-out =====

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnIdleEnd_EndsNoApplicationSignInAtOnce(bool applicationSessionsEndWithSsoSession)
    {
        // No cascade runs for an idle end, with the tree model on or off. On, the
        // application's chain ends at its next refresh instead (D-155-9; the refusal is
        // IdpSessionIdleTimelineTests' TC-1); off, it lives its own lifetime (D-155-1 (a)).
        _settings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var x = SignIn();
        Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Reader().IsLiveAsync(x, CancellationToken.None)).Should().BeFalse();

        _idpSessions[x].RevokedAt.Should().NotBeNull("the read ended it idle");
        ShouldBeLive("crm", "an idle end is not a sign-out: nothing is revoked at the read");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===== T7: the switch off is the behaviour before OI-155 =====

    [Fact]
    public async Task SwitchOff_ASignOutEndsOnlyTheBrowsersSession()
    {
        _settings.SingleLogoutEnabled = false;
        var x = SignIn();
        var console = Issue(x, applicationId: null, "console")!.Value;
        Issue(x, _application, "crm");
        Issue(x, applicationId: null, "accounts");

        await SignOutOfTheConsole(console);
        await new ConfirmEndSessionCommandHandler(Revocation(), NullLogger<ConfirmEndSessionCommandHandler>.Instance)
            .Handle(new ConfirmEndSessionCommand(BrowserX), CancellationToken.None);

        _idpSessions[x].RevokedAt.Should().NotBeNull();
        ShouldBeLive("crm", "off, an application's tokens are its own to discard");
        ShouldBeLive("accounts", "off, a platform app's own chain ends at its next refresh, not here");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SwitchOff_AnotherPersonSigningIn_EndsOnlyTheOldSession()
    {
        _settings.SingleLogoutEnabled = false;
        var x = SignIn();
        Issue(x, _application, "crm");

        await Revocation().ReplaceIdpSessionAsync(BrowserX, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        _idpSessions[x].RevokedAt.Should().NotBeNull();
        ShouldBeLive("crm", "off, as before OI-155");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SwitchOff_TheSamePersonAfterIdle_MovesNothing(bool applicationSessionsEndWithSsoSession)
    {
        _settings.SingleLogoutEnabled = false;
        _settings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var x = SignIn();
        Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, Guid.NewGuid(), CancellationToken.None);

        Token("crm").IdpSessionId.Should().Be(x, "off, a chain moves only from a live session, as before OI-155");
    }

    [Theory]
    [InlineData(CookieSignOut.Console)]
    [InlineData(CookieSignOut.RefreshCookie)]
    [InlineData(CookieSignOut.ConfirmationPage)]
    public async Task SwitchOff_ASignOutOnASessionAReadAlreadyEnded_LooksNothingUp_AndEndsNothingMore(CookieSignOut path)
    {
        _settings.SingleLogoutEnabled = false;
        var x = SignIn();
        Issue(x, applicationId: null, "console");
        Issue(x, _application, "crm");
        var endedIdle = await AReadEndsItIdle(x);

        await SignOutBy(path, "console");

        ShouldBeLive("crm", "off, as before PR B1");
        _idpSessions.LookupsIncludingRevoked.Should().Be(0, "off, a revoked session is not looked up at all");
        _idpSessions[x].RevokedAt.Should().Be(endedIdle);
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task SwitchOff_ASignInAfterAReadEndedThePreviousSessionIdle_EndsAndMovesNothing(
        bool samePerson, bool applicationSessionsEndWithSsoSession)
    {
        _settings.SingleLogoutEnabled = false;
        _settings.ApplicationSessionsEndWithSsoSession = applicationSessionsEndWithSsoSession;
        var x = SignIn();
        Issue(x, _application, "crm");
        await AReadEndsItIdle(x);

        await Revocation().ReplaceIdpSessionAsync(
            BrowserX, samePerson ? _person : Guid.NewGuid(), SignIn(BrowserY), CancellationToken.None);

        ShouldBeLive("crm", "off, as before PR B1");
        Token("crm").IdpSessionId.Should().Be(x, "off, nothing moves from a revoked session");
        _idpSessions.LookupsIncludingRevoked.Should().Be(0, "off, a revoked session is not looked up at all");
    }

    [Fact]
    public async Task SwitchOff_EndSignInsBornFromIdpSession_EndsNothing()
    {
        _settings.SingleLogoutEnabled = false;
        var x = SignIn();
        Issue(x, _application, "crm");

        var ended = await Revocation().EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.ServerSignOut, _person);

        ended.Should().BeEmpty();
        ShouldBeLive("crm", "off, nothing ends");
    }

    // ===== T8: best effort, and never cancelled =====

    [Fact]
    public async Task AFailureEndingOneSession_IsLogged_AndTheOthersStillEnd()
    {
        var x = SignIn();
        var console = Issue(x, applicationId: null, "console")!.Value;
        var crm = Issue(x, _application, "crm")!.Value;
        var other = Issue(x, _otherApplication, "other");
        _sessionRows
            .Setup(r => r.TerminateAsync(crm, TokenRevocationReasons.SingleLogout, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("the database did not answer"));

        var act = () => SignOutOfTheConsole(console);

        await act.Should().NotThrowAsync("a sign-out never fails because one of the other sessions could not end");
        ShouldHaveEnded("other", other);
        Token("crm").IsRevoked.Should().BeTrue("its tokens went first; only its row failed");
        _blacklist.Verify(b => b.BlacklistSession(crm.ToString(), It.IsAny<DateTime>()), Times.Once,
            "the next read no longer finds it, so its access token must stop now");
        _publisher.Verify(p => p.Publish(
            It.Is<SingleLogoutCompletedEvent>(e => e.EndedSignInCount == 2), It.IsAny<CancellationToken>()), Times.Once,
            "REVIEW 84 F4: its tokens are revoked, so it counts as ended although its row failed");
    }

    [Fact]
    public async Task ASessionWhoseRowFails_IsStillInTheReturnedList()
    {
        var x = SignIn();
        var crm = Issue(x, _application, "crm")!.Value;
        _sessionRows
            .Setup(r => r.TerminateAsync(crm, TokenRevocationReasons.SingleLogout, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("the database did not answer"));

        var ended = await Revocation().EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        ended.Should().ContainSingle(e => e.SessionId == crm && e.ApplicationId == _application,
            "the list is what back-channel logout will notify: a session whose tokens died belongs in it");
    }

    [Fact]
    public async Task ASignOutWhoseCallerHasLeft_StillEndsTheBrowsersSessionAndItsSignIns_OnARepositoryThatHonoursCancellation()
    {
        // The SQL repositories pass the token to OpenAsync, so a cancelled one throws there.
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        var revocation = Revocation(idpSessions: HonouringIdpSessions());
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await revocation.RevokeIdpSessionAsync(BrowserX, caller.Token);

        _idpSessions[x].RevokedAt.Should().NotBeNull("the browser's session ends although the caller left");
        ShouldHaveEnded("crm", crm);
    }

    [Fact]
    public async Task ASignOutWhoseCallerHasLeft_StillCompletesTheCascade_WithoutTheCallersToken()
    {
        var x = SignIn();
        var crm = Issue(x, _application, "crm")!.Value;
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Revocation().RevokeIdpSessionAsync(BrowserX, caller.Token);

        ShouldHaveEnded("crm", crm);
        _sessionRows.Verify(r => r.TerminateAsync(crm, TokenRevocationReasons.SingleLogout, CancellationToken.None), Times.Once,
            "the cascade runs without the caller's token, so a client that leaves does not leave it half done");
        _sessionRows.Verify(r => r.TerminateAsync(It.IsAny<Guid>(), It.IsAny<string>(), caller.Token), Times.Never);
    }

    /// <summary>
    /// The in-memory IdP sessions behind a mock that throws on a cancelled token, as the SQL
    /// repository does at OpenAsync. Strict, so a call this path should not make shows.
    /// </summary>
    private IIdpSessionRepository HonouringIdpSessions()
    {
        var honouring = new Mock<IIdpSessionRepository>(MockBehavior.Strict);
        honouring
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string hash, CancellationToken ct) => ct.IsCancellationRequested
                ? Task.FromCanceled<IdpSession?>(ct)
                : _idpSessions.GetByTokenHashAsync(hash, ct));
        honouring
            .Setup(r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .Returns((IdpSession s, CancellationToken ct) => ct.IsCancellationRequested
                ? Task.FromCanceled(ct)
                : _idpSessions.UpdateAsync(s, ct));
        return honouring.Object;
    }

    [Fact]
    public async Task ACookieSignOutWhoseBrowserHasLeft_StillEndsTheApplicationSessionsBornThere()
    {
        // REVIEW 84 F5: the refresh-cookie sign-out reads the token before anything else, and the
        // SQL repository honours a cancelled token there.
        var x = SignIn();
        Issue(x, applicationId: null, "accounts");
        var crm = Issue(x, _application, "crm");
        var tokens = new InstrumentedRefreshTokens(_tokens) { HonourCancellation = true };
        var handler = new LogoutWithRefreshCookieCommandHandler(
            tokens, _keys.Object, Revocation(tokens, HonouringIdpSessions()), _publisher.Object,
            NullLogger<LogoutWithRefreshCookieCommandHandler>.Instance);
        using var browser = new CancellationTokenSource();
        await browser.CancelAsync();

        var result = await handler.Handle(new LogoutWithRefreshCookieCommand("accounts", null, BrowserX), browser.Token);

        result.Value.Ended.Should().BeTrue();
        _idpSessions[x].RevokedAt.Should().NotBeNull();
        ShouldHaveEnded("crm", crm);
    }

    [Fact]
    public async Task AConsoleSignOutThatPresentsAnotherPersonsCookie_DoesNotEndThatPersonsApplications()
    {
        // REVIEW 84 F6: a console tab A left open, in a browser where B has signed in since.
        var b = Guid.NewGuid();
        var x = SignIn(person: b);
        Issue(x, _application, "crm-of-b", person: b);
        var consoleOfA = Issue(idpSessionId: null, applicationId: null, "console-of-a")!.Value;

        await SignOutOfTheConsole(consoleOfA);

        ShouldBeLive("crm-of-b", "A signing out ends no application of B");
        _idpSessions[x].RevokedAt.Should().NotBeNull("the SSO session in that browser still ends, as before OI-155");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AConsoleSignOutThatPresentsAnotherPersonsCookie_OfASessionAReadEnded_DoesNotEndThatPersonsApplications()
    {
        // PR B1: the lookup now also finds a revoked session, and the guard holds for it too.
        var b = Guid.NewGuid();
        var x = SignIn(person: b);
        Issue(x, _application, "crm-of-b", person: b);
        await AReadEndsItIdle(x);
        var consoleOfA = Issue(idpSessionId: null, applicationId: null, "console-of-a")!.Value;

        await SignOutOfTheConsole(consoleOfA);

        ShouldBeLive("crm-of-b", "A signing out ends no application of B");
        _idpSessions.LookupsIncludingRevoked.Should().Be(1, "the revoked session was found, then spared by the guard");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AReadThatFails_IsRetried_WithinTheCap()
    {
        // REVIEW 84 F7: a failed read proves nothing, so the cascade reads again instead of stopping.
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        var flaky = new InstrumentedRefreshTokens(_tokens) { FailingReads = 1 };

        var ended = await Revocation(flaky).EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        ended.Should().ContainSingle();
        ShouldHaveEnded("crm", crm);
        flaky.Reads.Should().Be(4, "the failed read, the one that found it, then two that found nothing");
    }

    // ===== T10: one event per cascade that ended something =====

    [Fact]
    public async Task ACascadeThatEndedSomething_PublishesOneEvent_WithoutTheCallersToken()
    {
        var x = SignIn();
        Issue(x, _application, "crm");
        Issue(x, _otherApplication, "legacy", withSession: false);

        var ended = await Revocation().EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        ended.Should().HaveCount(2);
        ended.Should().ContainSingle(e => e.SessionId == null && e.ApplicationId == _otherApplication,
            "a token issued without a session is revoked alone, and named");
        ShouldHaveEnded("legacy", sessionId: null);
        _publisher.Verify(p => p.Publish(
            It.Is<SingleLogoutCompletedEvent>(e => e.IdpSessionId == x && e.EndedSignInCount == 2 && e.Trigger == SingleLogoutTriggers.SignOut),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ACascadeThatEndedNothing_PublishesNoEvent()
    {
        var x = SignIn();

        (await Revocation().EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person)).Should().BeEmpty();

        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFailingPublish_DoesNotFailTheSignOut()
    {
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        _publisher
            .Setup(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("a handler threw"));

        var act = () => Revocation().RevokeIdpSessionAsync(BrowserX, CancellationToken.None);

        await act.Should().NotThrowAsync();
        ShouldHaveEnded("crm", crm);
    }

    // ===== T11: a refresh rotating a token while the cascade runs =====

    [Fact]
    public async Task ARotationCommittedBetweenTheReadAndTheRevoke_IsCaughtByTheSessionRevoke()
    {
        // The read returned the old token only; the rotation commits before the revoke runs, so
        // only the revoke by session id can reach the replacement (REVIEW 84 F3).
        var x = SignIn();
        var crm = Issue(x, _application, "crm")!.Value;
        var racing = new InstrumentedRefreshTokens(_tokens, "crm", "crm-next") { RotateOnFirstSessionRevoke = true };

        await Revocation(racing).EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        racing.ReplacementRevokedByTheRacingRevoke.Should().BeTrue("the replacement keeps the session id, which the revoke names");
        Token("crm-next").SessionId.Should().Be(crm);
        racing.Reads.Should().Be(3, "the read that found the old token, then two that found nothing: no read caught the replacement");
    }

    [Fact]
    public async Task ARotationTheFirstReadWaitedOn_AndWhoseReplacementItMissed_IsCaughtByTheNextRead()
    {
        // The read met the row the rotation had locked, waited for its commit, then found the old
        // token revoked and missed the replacement, which sat where the read had already looked.
        var x = SignIn();
        var crm = Issue(x, _application, "crm")!.Value;
        var racing = new InstrumentedRefreshTokens(_tokens, "crm", "crm-next") { RotateOnRead = 1, HideReplacementOnThatRead = true };

        var ended = await Revocation(racing).EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        Token("crm-next").IsRevoked.Should().BeTrue("the cascade ends only after two reads in a row find nothing");
        ended.Should().ContainSingle(e => e.SessionId == crm);
        racing.Reads.Should().Be(4, "the read that missed it, the one that found it, then two that found nothing");
    }

    // ===== REVIEW 88 F2: the console's sign-out after the idle limit, without its refresh cookie =====

    private LogoutWithIdpSessionCookieCommandHandler CookieOnlySignOut() =>
        new(Revocation(), NullLogger<LogoutWithIdpSessionCookieCommandHandler>.Instance);

    /// <summary>The session row the signing-out app names, as the table holds it (any state).</summary>
    private void SessionRow(Guid sessionId, Guid? person = null) =>
        _sessionRows
            .Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(id: sessionId, userId: person ?? _person));

    private Task<ErrorOr<LogoutWithIdpSessionCookieResult>> SignOutWithoutTheRefreshCookie(Guid sessionId) =>
        CookieOnlySignOut().Handle(new LogoutWithIdpSessionCookieCommand(sessionId, BrowserX), CancellationToken.None);

    [Fact]
    public async Task AConsoleSignOutWithoutItsRefreshCookie_AfterTheIdleLimit_EndsTheApplicationsBornThere()
    {
        // (i) The console's refresh was refused once the session went idle (that refusal is the
        // read that ended it) and deleted its refresh cookie; its sign-out names its own session.
        var x = SignIn();
        var console = Issue(x, applicationId: null, "console")!.Value;
        var crm = Issue(x, _application, "crm");
        var endedIdle = await AReadEndsItIdle(x);
        SessionRow(console);

        var result = await SignOutWithoutTheRefreshCookie(console);

        result.Value.IdpSessionEnded.Should().BeTrue("the SSO cookie can go");
        ShouldHaveEnded("crm", crm);
        _idpSessions[x].RevokedAt.Should().Be(endedIdle, "a revoked session is not revoked again");
        _publisher.Verify(p => p.Publish(
            It.Is<SingleLogoutCompletedEvent>(e => e.IdpSessionId == x && e.Trigger == SingleLogoutTriggers.SignOut &&
                                                   e.TriggeredBy == _person),
            CancellationToken.None), Times.Once, "one audit row");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ACookieOnlySignOutNamingAnotherPersonsSession_EndsNothing_NotEvenTheirSsoSession(bool theirSessionWentIdle)
    {
        // (ii) A's console tab, left open in a browser where B has signed in since.
        var b = Guid.NewGuid();
        var x = SignIn(person: b);
        Issue(x, _application, "crm-of-b", person: b);
        var revokedAt = theirSessionWentIdle ? await AReadEndsItIdle(x) : (DateTime?)null;
        var consoleOfA = Guid.NewGuid();
        SessionRow(consoleOfA);

        var result = await SignOutWithoutTheRefreshCookie(consoleOfA);

        result.Value.IdpSessionEnded.Should().BeFalse("B's cookie stays");
        _idpSessions[x].RevokedAt.Should().Be(revokedAt, "B's SSO session is B's: A's sign-out does not touch it");
        ShouldBeLive("crm-of-b", "A signing out ends nothing of B");
        _publisher.Verify(p => p.Publish(It.IsAny<SingleLogoutCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ACookieOnlySignOutNamingNoKnownSession_EndsNothing()
    {
        // (iii) A session id the table does not hold proves no one.
        var x = SignIn();
        Issue(x, _application, "crm");

        var result = await SignOutWithoutTheRefreshCookie(Guid.NewGuid());

        result.Value.IdpSessionEnded.Should().BeFalse();
        _idpSessions[x].RevokedAt.Should().BeNull();
        ShouldBeLive("crm", "an unknown session names no person");
        _idpSessions.LiveLookups.Should().Be(0, "the session row is read first, and named no one");
    }

    [Fact]
    public async Task SwitchOff_ACookieOnlySignOut_LooksNothingUp_AndEndsNothing()
    {
        // (iv) Off, exactly as before this path existed.
        _settings.SingleLogoutEnabled = false;
        var x = SignIn();
        var console = Issue(x, applicationId: null, "console")!.Value;
        Issue(x, _application, "crm");
        SessionRow(console);

        var result = await SignOutWithoutTheRefreshCookie(console);

        result.Value.IdpSessionEnded.Should().BeFalse();
        _idpSessions[x].RevokedAt.Should().BeNull("off, a sign-out without its refresh cookie ends nothing");
        ShouldBeLive("crm", "off, as before");
        _sessionRows.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _idpSessions.LiveLookups.Should().Be(0);
        _idpSessions.LookupsIncludingRevoked.Should().Be(0);
    }

    [Fact]
    public async Task ACookieOnlySignOut_OnALiveSessionOfThatPerson_EndsIt_AndTheApplicationsBornThere()
    {
        // (v) The person asked to sign out: their SSO session ends too.
        var x = SignIn();
        var accounts = Guid.NewGuid();
        var crm = Issue(x, _application, "crm");
        SessionRow(accounts);

        var result = await SignOutWithoutTheRefreshCookie(accounts);

        result.Value.IdpSessionEnded.Should().BeTrue();
        _idpSessions[x].RevokedAt.Should().NotBeNull();
        ShouldHaveEnded("crm", crm);
    }

    // ===== REVIEW 88 F3: a code redeemed across a sign-out, and a chain written during a replace =====

    private const string RedirectUri = "https://crm.example.com/callback";
    private static readonly string Verifier = new('v', 43);

    private static string S256(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task ACodeWhoseChainIsWrittenAfterASignOutsCascade_EndsThatChain_AndIssuesNothing()
    {
        var x = SignIn();
        var application = TestHelpers.CreateApplication(code: "CRM");
        var code = AuthorizationCode.Create(
            application.Id, _person, "hash:the-code", RedirectUri, S256(Verifier),
            TimeSpan.FromSeconds(60), "198.51.100.7", ScopeSet.OpenIdOnly, x);
        var codes = new Mock<IAuthorizationCodeRepository>();
        codes.Setup(r => r.ConsumeByCodeHashAsync("hash:the-code", It.IsAny<CancellationToken>())).ReturnsAsync(code);
        var applications = new Mock<IApplicationRepository>();
        applications.Setup(r => r.GetByCodeAsync("CRM", It.IsAny<CancellationToken>())).ReturnsAsync(application);
        var access = new Mock<IApplicationAccessRepository>();
        access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByIdAsync(_person, It.IsAny<CancellationToken>())).ReturnsAsync(TestHelpers.CreateUser(id: _person));
        Guid? late = null;
        var builder = new Mock<ILoginResponseBuilder>();
        builder
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .Returns(async () =>
            {
                // A sign-out in that browser commits, and its cascade reads the chains, all after the
                // exchange's first read and before this chain is written.
                await Revocation().RevokeIdpSessionAsync(BrowserX, CancellationToken.None);
                late = Issue(x, application.Id, "late");
                return (ErrorOr<LoginResponse>)new LoginResponse
                {
                    SessionId = late,
                    Token = new TokenResponse { AccessToken = "access", RefreshToken = "late", ExpiresIn = 900, RefreshExpiresIn = 604800 },
                };
            });
        var exchange = new ExchangeAuthorizationCodeCommandHandler(
            codes.Object, applications.Object, access.Object, users.Object, _idpSessions, _keys.Object,
            builder.Object, Revocation(), Mock.Of<IClientAuthenticator>(),
            NullLogger<ExchangeAuthorizationCodeCommandHandler>.Instance);

        var result = await exchange.Handle(
            new ExchangeAuthorizationCodeCommand("the-code", RedirectUri, "CRM", Verifier, "198.51.100.7", "agent"),
            CancellationToken.None);

        result.IsError.Should().BeTrue("the sign-out ended the session the code came from");
        result.FirstError.Code.Should().Be(AuthErrors.AuthorizationCodeInvalid.Code);
        ShouldHaveEnded("late", late);
        codes.Verify(r => r.RecordIssuedSessionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AChainWrittenFromTheOldSessionWhileTheSamePersonReplacesIt_MovesToTheNewSession()
    {
        // The code's chain lands after the first move read the chains and before the revocation, so
        // the exchange's second read still found the old session live: only the second move reaches it.
        var x = SignIn();
        Issue(x, _application, "crm");
        var replacement = SignIn(BrowserY);
        var idpSessions = new Mock<IIdpSessionRepository>(MockBehavior.Strict);
        idpSessions
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string hash, CancellationToken ct) => _idpSessions.GetByTokenHashAsync(hash, ct));
        idpSessions
            .Setup(r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .Returns((IdpSession session, CancellationToken ct) =>
            {
                Issue(x, _application, "late");
                return _idpSessions.UpdateAsync(session, ct);
            });

        await Revocation(idpSessions: idpSessions.Object)
            .ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);

        Token("crm").IdpSessionId.Should().Be(replacement, "the first move");
        Token("late").IdpSessionId.Should().Be(replacement,
            "the second move, after the revocation, reaches the chain written meanwhile");
        ShouldBeLive("late", "nothing ends for the same person");
    }

    // ===== REVIEW 88 F7: the lookup of revoked sessions runs only on a miss =====

    [Fact]
    public async Task ASignOutOnALiveSession_LooksUpNoRevokedSession()
    {
        var x = SignIn();
        var console = Issue(x, applicationId: null, "console")!.Value;
        var crm = Issue(x, _application, "crm");

        await SignOutOfTheConsole(console);

        ShouldHaveEnded("crm", crm);
        _idpSessions.LookupsIncludingRevoked.Should().Be(0, "the live lookup found the session");
    }

    /// <summary>
    /// The in-memory table, instrumented. A refresh can rotate one token (<c>from</c> into
    /// <c>to</c>) during the cascade's read number <see cref="RotateOnRead"/>: the rotation commits,
    /// then the read answers either the table as it now is, or, as a read that waited on the
    /// rotation's lock and had already passed the place of the replacement, the table without
    /// either token. Or it can rotate inside the first revoke by session id, after the read.
    /// With <see cref="HonourCancellation"/>, every call throws on a cancelled token, as the SQL
    /// repository does at <c>OpenAsync</c>.
    /// </summary>
    private sealed class InstrumentedRefreshTokens(InMemoryRefreshTokenRepository inner, string from = "", string to = "")
        : IRefreshTokenRepository
    {
        public int RotateOnRead { get; init; }
        public bool HideReplacementOnThatRead { get; init; }
        public bool RotateOnFirstSessionRevoke { get; init; }
        public bool HonourCancellation { get; init; }
        public int FailingReads { get; init; }

        public int Reads { get; private set; }
        public bool ReplacementRevokedByTheRacingRevoke { get; private set; }
        private bool _rotatedOnRevoke;

        private void Check(CancellationToken cancellationToken)
        {
            if (HonourCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async Task<RefreshToken> RotateAsync(CancellationToken cancellationToken)
        {
            var old = (await inner.GetByTokenHashAsync("hash:" + from, cancellationToken))!;
            var replacement = TestHelpers.CreateRefreshToken(
                userId: old.UserId, tokenHash: "hash:" + to, sessionId: old.SessionId, applicationId: old.ApplicationId,
                scope: old.Scope, expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: old.IdpSessionId);
            old.Revoke(old.UserId, TokenRevocationReasons.Rotated, replacement.TokenHash);
            (await inner.TryRotateAsync(old, replacement, cancellationToken)).Should().BeTrue();
            return replacement;
        }

        public async Task<IReadOnlyList<RefreshToken>> GetLiveByIdpSessionAsync(Guid userId, Guid idpSessionId, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            Reads++;
            if (Reads <= FailingReads)
            {
                throw new TimeoutException("the database did not answer");
            }

            if (Reads != RotateOnRead)
            {
                return await inner.GetLiveByIdpSessionAsync(userId, idpSessionId, cancellationToken);
            }

            var replacement = await RotateAsync(cancellationToken);
            var live = await inner.GetLiveByIdpSessionAsync(userId, idpSessionId, cancellationToken);
            return HideReplacementOnThatRead ? live.Where(t => t.TokenHash != replacement.TokenHash).ToList() : live;
        }

        public async Task RevokeBySessionIdAsync(Guid sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            if (!RotateOnFirstSessionRevoke || _rotatedOnRevoke)
            {
                await inner.RevokeBySessionIdAsync(sessionId, revokedBy, reason, cancellationToken);
                return;
            }

            _rotatedOnRevoke = true;
            var replacement = await RotateAsync(cancellationToken);
            await inner.RevokeBySessionIdAsync(sessionId, revokedBy, reason, cancellationToken);
            ReplacementRevokedByTheRacingRevoke = inner.ByHash(replacement.TokenHash).IsRevoked;
        }

        public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);
        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            return inner.GetByTokenHashAsync(tokenHash, cancellationToken);
        }
        public Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken) => inner.CreateAsync(token, cancellationToken);
        public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken)
        {
            Check(cancellationToken);
            return inner.UpdateAsync(token, cancellationToken);
        }
        public Task<bool> TryRotateAsync(RefreshToken revokedOld, RefreshToken replacement, CancellationToken cancellationToken) => inner.TryRotateAsync(revokedOld, replacement, cancellationToken);
        public Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeAllForUserAsync(userId, revokedBy, reason, cancellationToken);
        public Task RevokeByDeviceAsync(Guid userId, string deviceInfo, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeByDeviceAsync(userId, deviceInfo, revokedBy, reason, cancellationToken);
        public Task<IReadOnlyList<Guid>> RevokeAllForApplicationAsync(Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeAllForApplicationAsync(applicationId, revokedBy, reason, cancellationToken);
        public Task<IReadOnlyList<Guid>> RevokeForUserAndApplicationAsync(Guid userId, Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeForUserAndApplicationAsync(userId, applicationId, revokedBy, reason, cancellationToken);
        public Task<bool> HasLiveTokenInSessionAsync(Guid sessionId, CancellationToken cancellationToken) => inner.HasLiveTokenInSessionAsync(sessionId, cancellationToken);
        public Task<int> RelinkIdpSessionAsync(Guid userId, Guid fromIdpSessionId, Guid toIdpSessionId, CancellationToken cancellationToken) => inner.RelinkIdpSessionAsync(userId, fromIdpSessionId, toIdpSessionId, cancellationToken);
        public Task<int> RelinkApplicationChainsAsync(Guid userId, Guid fromIdpSessionId, Guid toIdpSessionId, CancellationToken cancellationToken) => inner.RelinkApplicationChainsAsync(userId, fromIdpSessionId, toIdpSessionId, cancellationToken);
        public Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.GetActiveTokensForUserAsync(userId, cancellationToken);
        public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) => inner.CleanupExpiredAsync(olderThanUtc, batchSize, cancellationToken);
    }
}
