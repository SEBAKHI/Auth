using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS2: which keys a lookup tries, in what order, and for how long (KR4). Rings
/// with several keys cannot be written by PR A in production; they are built here
/// so the windows PR B's transitions rely on are pinned before they are reachable.
/// </summary>
public class CredentialHashCandidatesTests
{
    private static readonly DateTime T = KeyringTestRings.Cutover;
    private static readonly TimeSpan Slack = PurposeLimitedOverlap.Slack;
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private static HmacKey Key(int id, DateTime? created = null, DateTime? demoted = null, DateTime? verifyUntil = null, bool compromised = false) =>
        new(id, Enumerable.Range(id * 32, 32).Select(i => (byte)i).ToArray(), created, verifyUntil, demoted, compromised);

    private static HmacKeyring Ring(int current, int? next, params HmacKey[] keys) =>
        HmacKeyring.Create(keys, current, next, legacyCutoverUtc: T, legacyVerifyUntilUtc: null).Value;

    private static IEnumerable<(int Id, bool Legacy)> Shape(HmacKeyring ring, CredentialPurpose purpose, DateTime now) =>
        ring.Accepted(purpose, now).Select(d => (d.Key.Id, d.IsLegacy));

    [Fact]
    public void Accepted_IsCurrentThenNextThenPreviousThenLegacy_WithoutRepeats()
    {
        var ring = Ring(3, 4,
            Key(1, demoted: T),
            Key(2, created: T.AddDays(-1), demoted: T.AddDays(9), verifyUntil: T.AddDays(39)),
            Key(3, created: T.AddDays(8)),
            Key(4, created: T.AddDays(10)));

        Shape(ring, CredentialPurpose.RefreshToken, T.AddDays(10).AddMinutes(1))
            .Should().Equal((3, false), (4, false), (2, false), (1, true));

        var hasher = TestCredentialHashers.Over(ring, new FixedTimeProvider(T.AddDays(10).AddMinutes(1)));
        var candidates = hasher.Candidates(CredentialPurpose.RefreshToken, "token").Values;
        candidates.Should().HaveCount(4).And.OnlyHaveUniqueItems();
        candidates[0].Should().Be(hasher.Hash(CredentialPurpose.RefreshToken, "token"));
    }

    [Fact]
    public void Previous_IsAcceptedUntil_DemotionPlusThePurposeLimitPlusSlack_WhenThatIsEarlier()
    {
        // PasswordReset lives at most 1440 minutes; key 2's own VerifyUntilUtc is later.
        var demoted = T.AddDays(5);
        var ring = Ring(3, null, Key(1, demoted: T), Key(2, demoted: demoted, verifyUntil: demoted.AddDays(30)), Key(3));
        var bound = demoted + TimeSpan.FromMinutes(1440) + Slack;

        Shape(ring, CredentialPurpose.PasswordReset, bound).Should().Contain((2, false));
        Shape(ring, CredentialPurpose.PasswordReset, bound + OneSecond).Should().NotContain((2, false));
    }

    [Fact]
    public void Previous_IsAcceptedUntil_ItsVerifyUntil_WhenThatIsEarlier()
    {
        // Refresh tokens: the limit is the 30-day cap, so the key's own VerifyUntilUtc
        // (promote + the longest live token, KR4) decides.
        var demoted = T.AddDays(5);
        var verifyUntil = demoted.AddDays(7) + Slack;
        var ring = Ring(3, null, Key(1, demoted: T), Key(2, demoted: demoted, verifyUntil: verifyUntil), Key(3));

        Shape(ring, CredentialPurpose.RefreshToken, verifyUntil).Should().Contain((2, false));
        Shape(ring, CredentialPurpose.RefreshToken, verifyUntil + OneSecond).Should().NotContain((2, false));
    }

    [Fact]
    public void WebhookKey_UnderAPreviousKey_IsAcceptedForNinetyDays_IgnoringVerifyUntilAndCompromise()
    {
        // Owner decision §11 q2: a webhook key has no lifetime, so an old key keeps
        // validating it for WebhookKeyGraceDays, even after an emergency.
        var demoted = T.AddDays(5);
        var ring = Ring(3, null,
            Key(1, demoted: T),
            Key(2, demoted: demoted, verifyUntil: demoted, compromised: true),
            Key(3));
        var bound = demoted.AddDays(WebhookKeyGraceOverlap.WebhookKeyGraceDays);

        Shape(ring, CredentialPurpose.WebhookKey, bound).Should().Contain((2, false));
        Shape(ring, CredentialPurpose.WebhookKey, bound + OneSecond).Should().NotContain((2, false));
    }

    [Theory]
    [InlineData(CredentialPurpose.ExternalNonce)]
    [InlineData(CredentialPurpose.StepUpTicket)]
    [InlineData(CredentialPurpose.Otp)]
    [InlineData(CredentialPurpose.RefreshToken)]
    [InlineData(CredentialPurpose.Invitation)]
    public void ACompromisedKey_IsAcceptedForNothing_ButWebhookKeys(CredentialPurpose purpose)
    {
        var demoted = T.AddDays(5);
        var ring = Ring(3, null,
            Key(1, demoted: T, compromised: true),
            Key(2, demoted: demoted, verifyUntil: demoted.AddDays(30), compromised: true),
            Key(3));
        var now = demoted.AddMinutes(1);

        Shape(ring, purpose, now).Should().Equal((3, false));
        Shape(ring, CredentialPurpose.WebhookKey, now).Should().Equal((3, false), (2, false), (1, true));
    }

    [Fact]
    public void TheLegacyForm_ComesOnlyFromKey1()
    {
        // Key 1 retired (gone from the ring): nothing computes the legacy form any more,
        // inside the window or not; key 2 as previous is a subkey, never legacy.
        var ring = Ring(3, null, Key(2, demoted: T.AddDays(1), verifyUntil: T.AddDays(20)), Key(3));

        ring.Accepted(CredentialPurpose.WebhookKey, T.AddDays(2)).Should().OnlyContain(d => !d.IsLegacy);
        ring.Accepted(CredentialPurpose.RefreshToken, T.AddDays(2)).Should().OnlyContain(d => !d.IsLegacy);
    }

    [Theory]
    [InlineData(CredentialPurpose.RefreshToken, 30 * 24 * 60)]
    [InlineData(CredentialPurpose.Invitation, 7 * 24 * 60)]
    [InlineData(CredentialPurpose.PasswordReset, 1440)]
    [InlineData(CredentialPurpose.ExternalNonce, 10)]
    public void TheLegacyForm_IsAcceptedUntil_CutoverPlusTheLimitPlusSlack(CredentialPurpose purpose, int limitMinutes)
    {
        var ring = KeyringTestRings.Rotated();
        var bound = T + TimeSpan.FromMinutes(limitMinutes) + Slack;

        Shape(ring, purpose, bound).Should().Contain((1, true));
        Shape(ring, purpose, bound + OneSecond).Should().NotContain((1, true));
    }

    [Fact]
    public void TheLegacyForm_ForWebhookKeys_IsAcceptedForNinetyDaysAfterTheCutover()
    {
        var ring = KeyringTestRings.Rotated(key1Compromised: true, legacyVerifyUntil: T.AddDays(1));
        var bound = T.AddDays(WebhookKeyGraceOverlap.WebhookKeyGraceDays);

        Shape(ring, CredentialPurpose.WebhookKey, bound).Should().Contain((1, true));
        Shape(ring, CredentialPurpose.WebhookKey, bound + OneSecond).Should().NotContain((1, true));
    }

    [Fact]
    public void TheLegacyForm_StopsAtTheRecordedLegacyVerifyUntil()
    {
        var ring = KeyringTestRings.Rotated(legacyVerifyUntil: T.AddDays(3));

        Shape(ring, CredentialPurpose.RefreshToken, T.AddDays(3)).Should().Contain((1, true));
        Shape(ring, CredentialPurpose.RefreshToken, T.AddDays(3) + OneSecond).Should().NotContain((1, true));
    }

    [Fact]
    public void AStagedNextKey_VerifiesForThirtyDaysFromStaging_ThenDrops()
    {
        var staged = T.AddDays(2);
        var ring = Ring(2, 3, Key(1, demoted: T), Key(2), Key(3, created: staged));
        var bound = staged + CredentialOverlapRule.NextStagedWindow;

        Shape(ring, CredentialPurpose.Invitation, bound).Should().Contain((3, false));
        Shape(ring, CredentialPurpose.Invitation, bound + OneSecond).Should().NotContain((3, false));
        Shape(ring, CredentialPurpose.WebhookKey, bound + OneSecond).Should().NotContain((3, false));
    }

    [Fact]
    public void Hash_NeverUsesTheNextOrAPreviousKey()
    {
        var ring = Ring(2, 3, Key(1, demoted: T), Key(2), Key(3, created: T.AddDays(1)));

        ring.Issuing.Key.Id.Should().Be(2);
        ring.Issuing.IsLegacy.Should().BeFalse();
    }

    [Fact]
    public void Verify_AcceptsAFingerprintUnderAnyAcceptedKey_AndNothingElse()
    {
        var now = T.AddMinutes(1);
        var single = TestCredentialHashers.Over(KeyringTestRings.SingleKey());
        var rotated = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(now));
        var underKey1 = single.Hash(CredentialPurpose.ExternalNonce, "nonce");

        rotated.Verify(CredentialPurpose.ExternalNonce, "nonce", underKey1).Should().BeTrue("inside the legacy window");
        rotated.Verify(CredentialPurpose.ExternalNonce, "nonce", rotated.Hash(CredentialPurpose.ExternalNonce, "nonce")).Should().BeTrue();
        rotated.Verify(CredentialPurpose.ExternalNonce, "other", underKey1).Should().BeFalse();
        rotated.Verify(CredentialPurpose.StepUpTicket, "nonce", rotated.Hash(CredentialPurpose.ExternalNonce, "nonce"))
            .Should().BeFalse("a fingerprint of one purpose is not one of another");
        rotated.Verify(CredentialPurpose.ExternalNonce, "nonce", null).Should().BeFalse();
        rotated.Verify(CredentialPurpose.ExternalNonce, null, underKey1).Should().BeFalse();
    }

    [Theory]
    [InlineData(new[] { 1, 1 }, 1, null, "twice")]
    [InlineData(new[] { 1, 2 }, 3, null, "current key 3 is not one of its keys")]
    [InlineData(new[] { 1, 2 }, 2, 2, "next key 2 must be")]
    [InlineData(new[] { 1, 2 }, 2, 1, "next key 1 must be")]
    [InlineData(new[] { 1, 2 }, 2, 5, "next key 5 must be")]
    public void ARingNoTransitionWrites_IsRefused(int[] ids, int current, int? next, string reason)
    {
        var ring = HmacKeyring.Create(ids.Select(id => Key(id)), current, next, legacyCutoverUtc: T, legacyVerifyUntilUtc: null);

        ring.IsError.Should().BeTrue();
        ring.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        ring.FirstError.Description.Should().Contain(reason);
    }

    [Fact]
    public void ARingSnapshot_IsReadOncePerOperation()
    {
        // Swapping the ring between two operations changes what the next one sees, and
        // only that: an operation already holding candidates keeps its own.
        var holder = new HmacKeyringHolder(KeyringTestRings.SingleKey());
        var hasher = new Auth.Infrastructure.Security.KeyringCredentialHasher(holder, new FixedTimeProvider(T.AddMinutes(1)));
        var before = hasher.Candidates(CredentialPurpose.Invitation, "invite");

        holder.Replace(KeyringTestRings.Rotated());
        var after = hasher.Candidates(CredentialPurpose.Invitation, "invite");

        before.Values.Should().HaveCount(1);
        after.Values.Should().HaveCount(2).And.EndWith(before.Values[0]);
    }
}
