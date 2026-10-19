using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Features.Authentication.Logout;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-155 PR A: an explicit end of a browser's IdP session (a sign-out there, or another person
/// signing in on it) ends every sign-in born from that session, the applications' and the
/// platform apps' own, through the real revocation service over the in-memory tables. An idle or
/// lifetime end ends none (D-155-1 (a)), and the switch off restores the behaviour before OI-155.
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

    private CredentialRevocationService Revocation(IRefreshTokenRepository? tokens = null) => new(
        _sessionRows.Object, tokens ?? _tokens, _idpSessions, _blacklist.Object, _keys.Object,
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

    [Fact]
    public async Task ASignOut_OnASessionAReadAlreadyEnded_EndsNothingMore_KnownLimit()
    {
        // The cookie lookup sees only sessions not revoked (IdpSessionRepository.GetByTokenHashAsync,
        // on an index filtered the same way), so a session an earlier read ended idle no longer
        // names its chains. Reaching it needs a schema change: deviation D-155-4, not built here.
        var x = SignIn();
        Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));
        await Reader().IsLiveAsync(x, CancellationToken.None);

        await Revocation().RevokeIdpSessionAsync(BrowserX, CancellationToken.None);

        ShouldBeLive("crm", "the limit this test pins; an application learns of it no earlier than before OI-155");
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
    public async Task AnotherPersonSigningIn_AfterAReadEndedThePreviousSessionIdle_EndsNothingMore_KnownLimit()
    {
        // As ASignOut_OnASessionAReadAlreadyEnded_EndsNothingMore_KnownLimit (D-155-4).
        var x = SignIn();
        Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));
        await Reader().IsLiveAsync(x, CancellationToken.None);

        await Revocation().ReplaceIdpSessionAsync(BrowserX, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        ShouldBeLive("crm", "the limit this test pins");
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

    [Fact]
    public async Task TheSamePersonSigningInAgain_AfterIdle_MovesTheApplicationChainOnly()
    {
        var x = SignIn();
        Issue(x, _application, "crm");
        Issue(x, applicationId: null, "console");
        _clock.Advance(TimeSpan.FromMinutes(31));
        var replacement = Guid.NewGuid();

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);

        Token("crm").IdpSessionId.Should().Be(replacement,
            "an application's chain does not end with an idle session, so a later sign-out here must still reach it");
        Token("console").IdpSessionId.Should().Be(x, "a platform app's own chain ended with its idle session (OI-154)");
        ShouldBeLive("crm", "nothing ends for the same person");
    }

    [Fact]
    public async Task TheSamePersonSigningInAgain_ThenSigningOut_EndsTheMovedApplicationChain()
    {
        var x = SignIn();
        var crm = Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));
        var replacement = SignIn(BrowserY);

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, replacement, CancellationToken.None);
        await Revocation().RevokeIdpSessionAsync(BrowserY, CancellationToken.None);

        ShouldHaveEnded("crm", crm);
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

    // ===== T6: idle and lifetime ends end no application =====

    [Fact]
    public async Task AnIdleEnd_EndsNoApplicationSignIn()
    {
        var x = SignIn();
        Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Reader().IsLiveAsync(x, CancellationToken.None)).Should().BeFalse();

        _idpSessions[x].RevokedAt.Should().NotBeNull("the read ended it idle");
        ShouldBeLive("crm", "an idle end is not a sign-out (D-155-1 (a))");
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

    [Fact]
    public async Task SwitchOff_TheSamePersonAfterIdle_MovesNothing()
    {
        _settings.SingleLogoutEnabled = false;
        var x = SignIn();
        Issue(x, _application, "crm");
        _clock.Advance(TimeSpan.FromMinutes(31));

        await Revocation().ReplaceIdpSessionAsync(BrowserX, _person, Guid.NewGuid(), CancellationToken.None);

        Token("crm").IdpSessionId.Should().Be(x, "off, a chain moves only from a live session, as before OI-155");
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
        var x = SignIn();
        var crm = Issue(x, _application, "crm")!.Value;
        var racing = new RotatingRefreshTokens(_tokens, rotateOnRead: 1, hideReplacementOnThatRead: false, "crm", "crm-next");

        await Revocation(racing).EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        Token("crm-next").IsRevoked.Should().BeTrue("the replacement keeps the session id, which the revoke names");
        Token("crm-next").SessionId.Should().Be(crm);
    }

    [Fact]
    public async Task ARotationTheFirstReadWaitedOn_AndWhoseReplacementItMissed_IsCaughtByTheNextRead()
    {
        // The read met the row the rotation had locked, waited for its commit, then found the old
        // token revoked and missed the replacement, which sat where the read had already looked.
        var x = SignIn();
        var crm = Issue(x, _application, "crm")!.Value;
        var racing = new RotatingRefreshTokens(_tokens, rotateOnRead: 1, hideReplacementOnThatRead: true, "crm", "crm-next");

        var ended = await Revocation(racing).EndSignInsBornFromIdpSessionAsync(_person, x, SingleLogoutTriggers.SignOut, _person);

        Token("crm-next").IsRevoked.Should().BeTrue("the cascade ends only after two reads in a row find nothing");
        ended.Should().ContainSingle(e => e.SessionId == crm);
        racing.Reads.Should().Be(4, "the read that missed it, the one that found it, then two that found nothing");
    }

    /// <summary>
    /// The in-memory table with a refresh rotating one token during the cascade's read number
    /// <c>rotateOnRead</c>: the rotation commits, then the read answers either the table as it now
    /// is, or, as a read that waited on the rotation's lock and had already passed the place of the
    /// replacement, the table without either token.
    /// </summary>
    private sealed class RotatingRefreshTokens(
        InMemoryRefreshTokenRepository inner, int rotateOnRead, bool hideReplacementOnThatRead, string from, string to)
        : IRefreshTokenRepository
    {
        public int Reads { get; private set; }

        public async Task<IReadOnlyList<RefreshToken>> GetLiveByIdpSessionAsync(Guid userId, Guid idpSessionId, CancellationToken cancellationToken)
        {
            Reads++;
            if (Reads != rotateOnRead)
            {
                return await inner.GetLiveByIdpSessionAsync(userId, idpSessionId, cancellationToken);
            }

            var old = (await inner.GetByTokenHashAsync("hash:" + from, cancellationToken))!;
            var replacement = TestHelpers.CreateRefreshToken(
                userId: old.UserId, tokenHash: "hash:" + to, sessionId: old.SessionId, applicationId: old.ApplicationId,
                scope: old.Scope, expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: old.IdpSessionId);
            old.Revoke(old.UserId, TokenRevocationReasons.Rotated, replacement.TokenHash);
            (await inner.TryRotateAsync(old, replacement, cancellationToken)).Should().BeTrue();

            var live = await inner.GetLiveByIdpSessionAsync(userId, idpSessionId, cancellationToken);
            return hideReplacementOnThatRead ? live.Where(t => t.TokenHash != replacement.TokenHash).ToList() : live;
        }

        public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);
        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) => inner.GetByTokenHashAsync(tokenHash, cancellationToken);
        public Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken) => inner.CreateAsync(token, cancellationToken);
        public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken) => inner.UpdateAsync(token, cancellationToken);
        public Task<bool> TryRotateAsync(RefreshToken revokedOld, RefreshToken replacement, CancellationToken cancellationToken) => inner.TryRotateAsync(revokedOld, replacement, cancellationToken);
        public Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeAllForUserAsync(userId, revokedBy, reason, cancellationToken);
        public Task RevokeByDeviceAsync(Guid userId, string deviceInfo, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeByDeviceAsync(userId, deviceInfo, revokedBy, reason, cancellationToken);
        public Task RevokeBySessionIdAsync(Guid sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeBySessionIdAsync(sessionId, revokedBy, reason, cancellationToken);
        public Task<IReadOnlyList<Guid>> RevokeAllForApplicationAsync(Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeAllForApplicationAsync(applicationId, revokedBy, reason, cancellationToken);
        public Task<IReadOnlyList<Guid>> RevokeForUserAndApplicationAsync(Guid userId, Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeForUserAndApplicationAsync(userId, applicationId, revokedBy, reason, cancellationToken);
        public Task<bool> HasLiveTokenInSessionAsync(Guid sessionId, CancellationToken cancellationToken) => inner.HasLiveTokenInSessionAsync(sessionId, cancellationToken);
        public Task<int> RelinkIdpSessionAsync(Guid userId, Guid fromIdpSessionId, Guid toIdpSessionId, CancellationToken cancellationToken) => inner.RelinkIdpSessionAsync(userId, fromIdpSessionId, toIdpSessionId, cancellationToken);
        public Task<int> RelinkApplicationChainsAsync(Guid userId, Guid fromIdpSessionId, Guid toIdpSessionId, CancellationToken cancellationToken) => inner.RelinkApplicationChainsAsync(userId, fromIdpSessionId, toIdpSessionId, cancellationToken);
        public Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.GetActiveTokensForUserAsync(userId, cancellationToken);
        public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) => inner.CleanupExpiredAsync(olderThanUtc, batchSize, cancellationToken);
    }
}
