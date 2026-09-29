using Auth.Domain.Constants;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// <c>RefreshToken.IsWithinReplayGrace</c>: the one rule that lets a just-rotated
/// cookie token be answered once more. It must hold only for an ordinary rotation
/// that named its replacement, strictly inside the window, and it has no clock of
/// its own.
/// </summary>
public class RefreshTokenReplayGraceTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private static Auth.Domain.Entities.RefreshToken Token(
        DateTime? revokedAt, string? reason, string? replacedBy) =>
        TestHelpers.CreateRefreshToken(
            revokedAt: revokedAt, reasonRevoked: reason, replacedByTokenHash: replacedBy,
            expiresAt: Now.AddDays(7));

    [Fact]
    public void RotatedWithAReplacement_InsideTheWindow_IsEligible() =>
        Token(Now.AddSeconds(-29), TokenRevocationReasons.Rotated, "t1-hash")
            .IsWithinReplayGrace(Grace, Now).Should().BeTrue();

    [Theory]
    [InlineData(30)] // the window's edge is outside it
    [InlineData(31)]
    [InlineData(3600)]
    public void RotatedWithAReplacement_AtOrAfterTheWindow_IsNotEligible(int secondsAgo) =>
        Token(Now.AddSeconds(-secondsAgo), TokenRevocationReasons.Rotated, "t1-hash")
            .IsWithinReplayGrace(Grace, Now).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RotatedWithoutAReplacement_IsNeverEligible(string? replacedBy) =>
        Token(Now.AddSeconds(-1), TokenRevocationReasons.Rotated, replacedBy)
            .IsWithinReplayGrace(Grace, Now).Should().BeFalse(
                "a grace answer revokes the replacement with no successor, so that token is single-use");

    [Theory]
    [InlineData(TokenRevocationReasons.RefreshTokenReuse)]
    [InlineData("User logout")]
    [InlineData(null)]
    public void RevokedForAnyOtherReason_IsNotEligible(string? reason) =>
        Token(Now.AddSeconds(-1), reason, "t1-hash")
            .IsWithinReplayGrace(Grace, Now).Should().BeFalse();

    [Fact]
    public void LiveToken_IsNotEligible() =>
        Token(revokedAt: null, reason: null, replacedBy: null)
            .IsWithinReplayGrace(Grace, Now).Should().BeFalse();

    [Fact]
    public void TheRule_ReadsTheSuppliedTime_NotTheClock()
    {
        var token = Token(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), TokenRevocationReasons.Rotated, "t1-hash");

        token.IsWithinReplayGrace(Grace, new DateTime(2020, 1, 1, 0, 0, 10, DateTimeKind.Utc)).Should().BeTrue();
        token.IsWithinReplayGrace(Grace, new DateTime(2020, 1, 1, 0, 1, 0, DateTimeKind.Utc)).Should().BeFalse();
    }
}
