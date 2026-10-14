using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// OI-154: the one reader of the IdP session cookie over the table as its SQL
/// changes it (<see cref="InMemoryIdpSessionRepository"/>), on a clock the tests
/// walk. What each handler does with the answer is in its own tests and in
/// <c>IdpSessionIdleHandlerTests</c>.
/// </summary>
public class IdpSessionReaderTests
{
    private const string Token = "sso-token";

    private readonly AdjustableTimeProvider _clock = new();
    private readonly InMemoryIdpSessionRepository _table = new();
    private readonly IdentityProviderSettings _settings = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly CapturingLogger<IdpSessionReader> _logger = new();

    public IdpSessionReaderTests()
    {
        _keys.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns((string value) => "hash:" + value);
        _clock.Advance(TimeSpan.Zero);
    }

    private IdpSessionReader Reader() => new(
        _table, _keys.Object, TestHelpers.CreateOptions(_settings), _clock, _logger);

    /// <summary>A browser session (no "Stay signed in") signed in now.</summary>
    private Guid SignIn(bool persistent = false)
    {
        var id = Guid.NewGuid();
        _table.Seed(new InMemoryIdpSessionRepository.Row(
            id, Guid.NewGuid(), "hash:" + Token, _clock.UtcNow, _clock.UtcNow.AddHours(persistent ? 168 : 10),
            RevokedAt: null, IsPersistent: persistent, LastActivityAt: null));
        return id;
    }

    // --- T4: use keeps a browser session alive; idleness ends it ---------------

    [Fact]
    public async Task UsedEveryTwentyMinutesForTwoHours_StaysValid()
    {
        var id = SignIn();

        for (var step = 1; step <= 6; step++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None))
                .Should().NotBeNull($"used {step * 20} minutes after the sign-in, 20 minutes after the last use");
        }

        _table[id].LastActivityAt.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public async Task LeftForThirtyOneMinutes_IsNoLongerValid_AndIsRevokedOnce()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().BeNull();
        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().BeNull();

        _table[id].RevokedAt.Should().NotBeNull("the reader that finds a session dead of idleness ends it");
        _table.Revocations[id].Should().Be(1, "a revoked row is no longer found, so it is ended once");
        _logger.Entries.Should().Contain(entry => entry.Message.Contains("unused for 30 minutes"));
    }

    [Fact]
    public async Task OnceEndedForIdleness_RaisingTheLimit_DoesNotBringItBack()
    {
        SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));
        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Check, CancellationToken.None)).Should().BeNull();

        _settings.IdpBrowserSessionIdleMinutes = 120;

        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().BeNull(
            "the session was revoked when it was found idle; a longer limit cannot revive it");
    }

    [Fact]
    public async Task Inspect_WritesNothing_NotEvenTheEndOfAnIdleSession()
    {
        // The organization page's state is a query (CQRS): its read changes nothing.
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(10));
        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Inspect, CancellationToken.None)).Should().NotBeNull();
        _table[id].LastActivityAt.Should().BeNull();

        _clock.Advance(TimeSpan.FromMinutes(25));
        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Inspect, CancellationToken.None)).Should().BeNull();
        _table[id].RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Check_RecordsNoUse()
    {
        // End-session and the refresh cookie's lifetime read with Check.
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(20));

        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Check, CancellationToken.None)).Should().NotBeNull();

        _table[id].LastActivityAt.Should().BeNull();
        _clock.Advance(TimeSpan.FromMinutes(11));
        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Check, CancellationToken.None)).Should().BeNull(
            "reads that record no use do not keep a session alive");
    }

    [Fact]
    public async Task AZeroLimit_SwitchesTheIdleCheckOff_ForExistingSessions()
    {
        SignIn();
        _clock.Advance(TimeSpan.FromHours(3));
        _settings.IdpBrowserSessionIdleMinutes = 0;

        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task APersistentSession_HasNoIdleLimit()
    {
        SignIn(persistent: true);
        _clock.Advance(TimeSpan.FromDays(6));

        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown-token")]
    public async Task NoCookie_OrAnUnknownOne_IsNoSession(string? token)
    {
        SignIn();

        (await Reader().FindLiveAsync(token, IdpSessionReadMode.Use, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Use_WhenTheWriteFindsTheSessionEnded_IsNoSession()
    {
        // Signed out in another tab between the read and the write.
        var repository = new Mock<Auth.Domain.Interfaces.Repositories.IIdpSessionRepository>();
        repository
            .Setup(r => r.GetByTokenHashAsync("hash:" + Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestIdpSessions.SignedInNow(Guid.NewGuid(), "hash:" + Token, TimeSpan.FromHours(10)));
        repository
            .Setup(r => r.RecordActivityAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var reader = new IdpSessionReader(
            repository.Object, _keys.Object, TestHelpers.CreateOptions(_settings), TimeProvider.System,
            NullLogger<IdpSessionReader>.Instance);

        (await reader.FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().BeNull();
    }

    // --- T8: rows written before the deploy (both new columns NULL) ------------

    [Theory]
    [InlineData(31, false)]
    [InlineData(20, true)]
    public async Task ARowFromBeforeTheDeploy_IdlesOutLikeABrowserSession(int minutesSinceSignIn, bool valid)
    {
        var id = Guid.NewGuid();
        _table.Seed(new InMemoryIdpSessionRepository.Row(
            id, Guid.NewGuid(), "hash:" + Token, _clock.UtcNow.AddMinutes(-minutesSinceSignIn), _clock.UtcNow.AddDays(6),
            RevokedAt: null, IsPersistent: null, LastActivityAt: null));

        var session = await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None);

        (session is not null).Should().Be(valid);
    }

    // --- T14 and T15 at the reader: an application's refresh counts as use -----

    [Fact]
    public async Task RefreshesEveryTwentyMinutes_KeepTheSessionAlive_WithoutAnyAuthorize()
    {
        var id = SignIn();

        for (var step = 1; step <= 6; step++)
        {
            _clock.Advance(TimeSpan.FromMinutes(20));
            (await Reader().RecordUseAsync(id, CancellationToken.None)).Should().BeTrue();
        }

        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().NotBeNull(
            "two hours without an authorize, but in use the whole time (D-154-2 (b))");
    }

    [Fact]
    public async Task ARefreshAfterTheSessionWentIdle_DoesNotBringItBack()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Reader().RecordUseAsync(id, CancellationToken.None)).Should().BeFalse();

        _table[id].LastActivityAt.Should().BeNull("the write refuses a session that died idle");
        (await Reader().FindLiveAsync(Token, IdpSessionReadMode.Use, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ARefreshOfARevokedSession_RecordsNothing()
    {
        var id = SignIn();
        _table.Seed(_table[id] with { RevokedAt = _clock.UtcNow });

        (await Reader().RecordUseAsync(id, CancellationToken.None)).Should().BeFalse();
        _table[id].LastActivityAt.Should().BeNull();
    }

    // --- F4: ending an idle session is best effort ------------------------------

    [Theory]
    [InlineData(IdpSessionReadMode.Check)]
    [InlineData(IdpSessionReadMode.Use)]
    public async Task FoundIdle_WhenEndingItFails_IsStillNoSession_AndAWarning(IdpSessionReadMode mode)
    {
        // The answer is already known (the session is dead): a failed write must
        // not turn the sign-in page, or "no session", into a server error.
        var repository = new Mock<IIdpSessionRepository>();
        repository
            .Setup(r => r.GetByTokenHashAsync("hash:" + Token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdpSession(
                Guid.NewGuid(), Guid.NewGuid(), "hash:" + Token, _clock.UtcNow.AddMinutes(-31), _clock.UtcNow.AddHours(9),
                null, null, null));
        repository
            .Setup(r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));
        var reader = new IdpSessionReader(
            repository.Object, _keys.Object, TestHelpers.CreateOptions(_settings), _clock, _logger);

        (await reader.FindLiveAsync(Token, mode, CancellationToken.None)).Should().BeNull();

        _logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning)
            .Which.Message.Should().NotContain(Token, "the cookie value is a credential");
    }

    // --- D-74-1 (c): by id, for a platform app's refresh -----------------------

    [Fact]
    public async Task IsLive_ForALiveSession_IsTrue_AndRecordsNoUse()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(20));

        (await Reader().IsLiveAsync(id, CancellationToken.None)).Should().BeTrue();
        _table[id].LastActivityAt.Should().BeNull("the refresh records its use only once it is accepted");
    }

    [Fact]
    public async Task IsLive_ForASessionGoneIdle_IsFalse_AndEndsIt()
    {
        var id = SignIn();
        _clock.Advance(TimeSpan.FromMinutes(31));

        (await Reader().IsLiveAsync(id, CancellationToken.None)).Should().BeFalse();
        _table[id].RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task IsLive_ForARevokedSession_OrOnePastItsLifetime_IsFalse()
    {
        var revoked = SignIn(persistent: true);
        _table.Seed(_table[revoked] with { RevokedAt = _clock.UtcNow });
        var expired = SignIn(persistent: true);
        _table.Seed(_table[expired] with { ExpiresAt = _clock.UtcNow.AddMinutes(-1) });

        (await Reader().IsLiveAsync(revoked, CancellationToken.None)).Should().BeFalse("signed out, or replaced");
        (await Reader().IsLiveAsync(expired, CancellationToken.None)).Should().BeFalse("past its lifetime");
    }

    [Fact]
    public async Task IsLive_ForAPurgedSession_IsFalse()
    {
        (await Reader().IsLiveAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
    }
}
