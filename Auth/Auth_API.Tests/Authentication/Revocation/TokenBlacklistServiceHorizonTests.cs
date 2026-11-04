using System.Threading.Channels;
using Auth.Application.Configuration;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// X04, RV2 (a) (b) (c) (d) (i): every revocation is kept at least as long as the tokens it
/// covers can be accepted. Before X04 a user-level entry lived one hour while an access token
/// could live a day, so a token stolen before a sign-out everywhere worked again after the hour.
/// </summary>
/// <remarks>
/// Deliberate breaks, each seen red: the one-hour retention back in
/// <c>BlacklistAllUserTokens</c>; eviction one hour after the revocation; the max dropped from
/// the load; the session floor dropped; the skew dropped from the jti entry.
/// </remarks>
public sealed class TokenBlacklistServiceHorizonTests : IDisposable
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly AdjustableTimeProvider _clock = new();
    private readonly Channel<TokenRevocation> _queue = Channel.CreateUnbounded<TokenRevocation>();
    private readonly TokenBlacklistService _service;
    private readonly DateTime _now;

    public TokenBlacklistServiceHorizonTests()
    {
        _clock.Advance(TimeSpan.Zero);
        _now = _clock.UtcNow;
        _service = new TokenBlacklistService(_queue.Writer, _clock, NullLogger<TokenBlacklistService>.Instance);
    }

    public void Dispose() => _service.Dispose();

    private List<TokenRevocation> Queued()
    {
        var items = new List<TokenRevocation>();
        while (_queue.Reader.TryRead(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public void TheHorizon_IsTheLargestLifetimePlusTheLargestSkew()
    {
        RevocationHorizon.User.Should().Be(TimeSpan.FromMinutes(1445));
        RevocationHorizon.User.Should().Be(
            TimeSpan.FromMinutes(JwtSettings.MaxAccessTokenLifetimeMinutes) + TimeSpan.FromSeconds(JwtSettings.MaxClockSkewSeconds));
    }

    [Fact]
    public void A_UserEntry_IsStoredUntilTheHorizonAfterItTookEffect()
    {
        _service.BlacklistAllUserTokens(UserId, _now);

        var row = Queued().Should().ContainSingle(r => r.Type == RevocationType.User).Subject;
        row.EffectiveAt.Should().Be(_now);
        (row.ExpiresAt - row.EffectiveAt).Should().Be(TimeSpan.FromMinutes(1445), "86,700 seconds, not 3,600");
    }

    [Fact]
    public void B_UserEntry_StillRefusesAtTwoHours_AndIsEvictedOnlyAfterTheHorizon()
    {
        _service.BlacklistAllUserTokens(UserId, _now);
        var issuedBefore = _now.AddSeconds(-1);

        _clock.Advance(TimeSpan.FromHours(2));
        _service.CleanupExpiredEntries();
        _service.AreUserTokensBlacklisted(UserId, issuedBefore).Should()
            .BeTrue("a token issued before the revocation may live a day: two hours later it is still refused");

        _clock.Advance(RevocationHorizon.User - TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));
        _service.CleanupExpiredEntries();
        _service.AreUserTokensBlacklisted(UserId, issuedBefore).Should().BeTrue("one second before the horizon");

        _clock.Advance(TimeSpan.FromSeconds(2));
        _service.CleanupExpiredEntries();
        _service.AreUserTokensBlacklisted(UserId, issuedBefore).Should()
            .BeFalse("past the horizon every token the entry covers has expired, so it is dropped");
    }

    [Fact]
    public void C_ALoadedOneHourRow_CountsUntilTheHorizon_NotUntilItsStoredExpiry()
    {
        // A row written before X04: ExpiresAt one hour after EffectiveAt.
        var effectiveAt = _now.AddHours(-2);
        var stale = new TokenRevocation(RevocationType.User, UserId.ToString(), effectiveAt, effectiveAt.AddHours(1));

        _service.LoadSnapshot([stale]).Should().Be(1);
        _service.AreUserTokensBlacklisted(UserId, effectiveAt.AddSeconds(-1)).Should().BeTrue();

        _clock.Advance(TimeSpan.FromHours(3));
        _service.CleanupExpiredEntries();
        _service.AreUserTokensBlacklisted(UserId, effectiveAt.AddSeconds(-1)).Should()
            .BeTrue("the entry is kept until its EffectiveAt plus the horizon");
    }

    [Fact]
    public void C_ARowPastTheHorizon_IsNotLoaded()
    {
        var effectiveAt = _now - RevocationHorizon.User - TimeSpan.FromSeconds(1);
        var gone = new TokenRevocation(RevocationType.User, UserId.ToString(), effectiveAt, effectiveAt.AddHours(1));

        _service.LoadSnapshot([gone]).Should().Be(0);
        _service.AreUserTokensBlacklisted(UserId, effectiveAt.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void C_ALoad_NeverShortensWhatThisProcessRevoked()
    {
        _service.BlacklistAllUserTokens(UserId, _now);
        _service.BlacklistSession("sid-kept", _now);

        // An older copy of the same entries, as a periodic re-read could bring back.
        _service.LoadSnapshot(
        [
            new TokenRevocation(RevocationType.User, UserId.ToString(), _now.AddMinutes(-30), _now.AddMinutes(30)),
            new TokenRevocation(RevocationType.Session, "sid-kept", _now.AddMinutes(-30), _now.AddMinutes(1)),
        ]);

        _clock.Advance(TimeSpan.FromHours(2));
        _service.CleanupExpiredEntries();
        _service.AreUserTokensBlacklisted(UserId, _now.AddSeconds(-1)).Should().BeTrue();
        _service.IsSessionBlacklisted("sid-kept").Should().BeTrue();
    }

    [Fact]
    public void D_ASessionEntry_IsKeptAtLeastTheHorizonFromNow_WhateverTheCallerAsked()
    {
        // A session row's expiry, or now + the live lifetime: both can fall short of a token
        // issued under a longer lifetime before it was lowered.
        _service.BlacklistSession("sid-short", _now.AddMinutes(15));

        Queued().Should().ContainSingle(r => r.Key == "sid-short").Which.ExpiresAt
            .Should().Be(_now + RevocationHorizon.User);

        _clock.Advance(TimeSpan.FromHours(2));
        _service.CleanupExpiredEntries();
        _service.IsSessionBlacklisted("sid-short").Should().BeTrue("the floor holds in memory too");
    }

    [Fact]
    public void D_ASessionEntry_KeepsALaterValueTheCallerAsked()
    {
        var later = _now.AddDays(3);

        _service.BlacklistSession("sid-long", later);

        Queued().Should().ContainSingle(r => r.Key == "sid-long").Which.ExpiresAt.Should().Be(later);
    }

    [Fact]
    public void I_ATokenEntry_IsKeptUntilItsExpPlusTheLargestSkew()
    {
        var exp = _now.AddMinutes(15);

        _service.BlacklistToken("jti-skew", exp);

        Queued().Should().ContainSingle(r => r.Key == "jti-skew").Which.ExpiresAt
            .Should().Be(exp.AddSeconds(300), "validation accepts a token until exp plus the skew");

        _clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(200));
        _service.CleanupExpiredEntries();
        _service.IsTokenBlacklisted("jti-skew").Should().BeTrue("inside the skew after exp");
    }
}
