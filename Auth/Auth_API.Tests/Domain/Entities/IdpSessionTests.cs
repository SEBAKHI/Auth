using Auth.Application.Configuration;
using Auth.Domain.Entities;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// OI-154: the one rule for "this IdP session still signs the browser in"
/// (<see cref="IdpSession.IsValidAt"/>), and the lifetimes a new session gets.
/// </summary>
public class IdpSessionTests
{
    private static readonly DateTime SignedIn = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Idle30 = TimeSpan.FromMinutes(30);

    private static IdpSession Session(
        bool persistent = false,
        DateTime? lastActivityAt = null,
        DateTime? revokedAt = null,
        TimeSpan? lifetime = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "hash", SignedIn, SignedIn + (lifetime ?? TimeSpan.FromHours(10)),
            revokedAt, null, null, persistent, lastActivityAt);

    /// <summary>
    /// T1, as a table: (persistent, minutes since the sign-in to the last use or
    /// null for none, minutes since the sign-in to now, idle limit in minutes,
    /// valid).
    /// </summary>
    public static TheoryData<bool, double?, double, int, bool> Rule => new()
    {
        // A browser session, counted from its last use.
        { false, 60, 60 + 29 + 59 / 60.0, 30, true },
        { false, 60, 60 + 30 + 1 / 60.0, 30, false },
        { false, 60, 60 + 30, 30, false },
        // No use yet: counted from the sign-in.
        { false, null, 29 + 59 / 60.0, 30, true },
        { false, null, 30 + 1 / 60.0, 30, false },
        // A zero limit is no idle check at all.
        { false, null, 9 * 60, 0, true },
        // "Stay signed in": no idle limit; six days unused is still valid.
        { true, null, 6 * 24 * 60, 30, true },
    };

    [Theory]
    [MemberData(nameof(Rule))]
    public void IsValidAt_FollowsTheIdleRule(
        bool persistent, double? lastUseMinutes, double nowMinutes, int idleMinutes, bool valid)
    {
        var session = Session(
            persistent,
            lastUseMinutes is { } used ? SignedIn.AddMinutes(used) : null,
            lifetime: TimeSpan.FromDays(7));

        session.IsValidAt(SignedIn.AddMinutes(nowMinutes), TimeSpan.FromMinutes(idleMinutes)).Should().Be(valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsValidAt_ARevokedSession_IsInvalidEitherWay(bool persistent)
    {
        var session = Session(persistent, lastActivityAt: SignedIn.AddMinutes(1), revokedAt: SignedIn.AddMinutes(2));

        session.IsValidAt(SignedIn.AddMinutes(3), Idle30).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsValidAt_ASessionPastItsLifetime_IsInvalidEitherWay(bool persistent)
    {
        var session = Session(persistent, lifetime: TimeSpan.FromHours(10));
        var justUsed = SignedIn.AddHours(10).AddMinutes(-1);
        session.RecordActivity(justUsed);

        session.IsValidAt(SignedIn.AddHours(10), Idle30).Should().BeFalse("the absolute lifetime ends it");
        session.IsValidAt(SignedIn.AddHours(10).AddMinutes(-1), Idle30).Should().BeTrue();
    }

    [Fact]
    public void IsIdleAt_NeverForAPersistentSession_NorWithoutALimit()
    {
        Session(persistent: true).IsIdleAt(SignedIn.AddDays(6), Idle30).Should().BeFalse();
        Session().IsIdleAt(SignedIn.AddHours(9), TimeSpan.Zero).Should().BeFalse();
        Session().IsIdleAt(SignedIn.AddMinutes(30), Idle30).Should().BeTrue();
    }

    [Fact]
    public void RecordActivity_MovesTheIdleClock()
    {
        var session = Session();
        session.RecordActivity(SignedIn.AddMinutes(20));

        session.LastUsedAt.Should().Be(SignedIn.AddMinutes(20));
        session.IsValidAt(SignedIn.AddMinutes(49), Idle30).Should().BeTrue();
        session.IsValidAt(SignedIn.AddMinutes(51), Idle30).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_FixesTheLifetimeAndTheChoiceAtTheSignIn(bool persistent)
    {
        var id = Guid.NewGuid();

        var session = IdpSession.Create(
            id, Guid.NewGuid(), "hash", persistent, TimeSpan.FromHours(10), SignedIn, "127.0.0.1", "agent");

        session.Id.Should().Be(id);
        session.IsPersistent.Should().Be(persistent);
        session.CreatedAt.Should().Be(SignedIn);
        session.ExpiresAt.Should().Be(SignedIn.AddHours(10));
        session.LastActivityAt.Should().BeNull();
    }

    // --- T2: the lifetimes a new session gets ---------------------------------

    [Fact]
    public void Lifetime_StaySignedIn_IsTheSessionLifetimeInDays()
    {
        new IdentityProviderSettings().IdpSessionLifetimeFor(persistent: true).Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void Lifetime_ABrowserSession_IsTheBrowserSessionMaximum()
    {
        new IdentityProviderSettings().IdpSessionLifetimeFor(persistent: false).Should().Be(TimeSpan.FromHours(10));
    }

    [Fact]
    public void Lifetime_ABrowserSession_NeverOutlivesTheStaySignedInLifetime()
    {
        var settings = new IdentityProviderSettings { IdpSessionLifetimeDays = 1, IdpBrowserSessionLifetimeHours = 24 };

        settings.IdpSessionLifetimeFor(persistent: false).Should().Be(TimeSpan.FromDays(1));
        settings.IdpSessionLifetimeFor(persistent: true).Should().Be(TimeSpan.FromDays(1));

        new IdentityProviderSettings { IdpSessionLifetimeDays = 1, IdpBrowserSessionLifetimeHours = 5 }
            .IdpSessionLifetimeFor(persistent: false).Should().Be(TimeSpan.FromHours(5));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(48, 24)]
    public void Lifetime_AFileValueOutsideTheRange_IsHeldToIt(int hours, int expected)
    {
        new IdentityProviderSettings { IdpBrowserSessionLifetimeHours = hours }
            .IdpSessionLifetimeFor(persistent: false).Should().Be(TimeSpan.FromHours(expected));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(5000, 1440)]
    public void IdleLimit_IsHeldToZeroTo1440Minutes(int minutes, int expected)
    {
        new IdentityProviderSettings { IdpBrowserSessionIdleMinutes = minutes }
            .IdpBrowserSessionIdleLimit.Should().Be(TimeSpan.FromMinutes(expected));
    }
}
