using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS4 (KR3, KR4): the keyring's transitions and their refusals. Each test reads what
/// the new ring accepts for credentials of every purpose, because a transition that
/// writes the wrong date breaks nothing visible until the credentials it should have kept
/// stop working.
/// </summary>
public class HmacKeyringTransitionsTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    private static readonly CredentialPurpose[] Bounded = CredentialPurposePolicy.Purposes
        .Where(purpose => purpose != CredentialPurpose.WebhookKey)
        .ToArray();

    private static byte[] Material(int seed) => Enumerable.Range(seed, 32).Select(i => (byte)i).ToArray();

    private static HmacKeyring Staged() => KeyringTestRings.SingleKey().Stage(Material(100), T0).Value;

    [Fact]
    public void Stage_AddsTheNextKey_WhichVerifiesButDoesNotIssue()
    {
        var ring = Staged();

        ring.Next!.Id.Should().Be(2, "a staged key takes the id above every id in the ring, never 1");
        ring.Next.CreatedAtUtc.Should().Be(T0);
        ring.Current.Id.Should().Be(1);
        ring.Issuing.IsLegacy.Should().BeTrue("staging changes nothing anyone is issued");
        ring.Accepted(CredentialPurpose.RefreshToken, T0.AddDays(1)).Select(d => d.Key.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void Stage_WhenAKeyIsStaged_KeepsIt()
    {
        var ring = Staged();

        var again = ring.Stage(Material(200), T0.AddHours(1)).Value;

        again.Should().BeSameAs(ring, "KR3: stage returns the staged key");
    }

    [Fact]
    public void StageImport_OfMaterialTheRingHolds_IsRefused_Key1Included()
    {
        // A key 2 holding key 1's material would make key 1 a key-derivation key (RV-P4-3 by material).
        var result = KeyringTestRings.SingleKey().StageImport(KeyringTestRings.Key1Material, T0);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        result.FirstError.Description.Should().Contain("already HMAC key 1", "the transition refuses it, not only the ring check behind it");
    }

    [Fact]
    public void StageImport_WhileAnotherKeyIsStaged_IsRefused_ButTheSameKeyIsKept()
    {
        var ring = Staged();

        ring.StageImport(Material(200), T0).FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        ring.StageImport(Material(100), T0).Value.Should().BeSameAs(ring);
    }

    [Fact]
    public void Promote_WithoutAStagedKey_IsRefused()
    {
        var result = KeyringTestRings.SingleKey().Promote(T0, T0.AddDays(7));

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.KeyringNoNextKey");
    }

    [Fact]
    public void ThePromoteOfKey2_RecordsTheCutover_AndKeepsKey1VerifyingThroughItsWindow()
    {
        var verifyUntil = T0.AddDays(20);

        var ring = Staged().Promote(T0, verifyUntil).Value;

        ring.Current.Id.Should().Be(2);
        ring.Next.Should().BeNull();
        ring.Issuing.IsLegacy.Should().BeFalse();
        ring.LegacyCutoverUtc.Should().Be(T0, "the promote that stops key 1 issuing is the cutover (D-S11B-1)");
        ring.LegacyVerifyUntilUtc.Should().Be(verifyUntil);
        ring.Find(1)!.DemotedAtUtc.Should().Be(T0);
        ring.Find(1)!.VerifyUntilUtc.Should().Be(verifyUntil);

        foreach (var purpose in Bounded)
        {
            ring.Accepted(purpose, T0.AddMinutes(1)).Should().Contain(d => d.IsLegacy,
                $"a {purpose} issued a minute before the promote still verifies after it");
        }

        ring.Accepted(CredentialPurpose.RefreshToken, verifyUntil.AddSeconds(1)).Should().NotContain(d => d.IsLegacy);
    }

    [Fact]
    public void ASecondPromote_DemotesKey2_AndKeepsTheLegacyWindowAsRecorded()
    {
        var first = Staged().Promote(T0, T0.AddDays(20)).Value;
        var second = first.Stage(Material(200), T0.AddDays(1)).Value.Promote(T0.AddDays(2), T0.AddDays(9)).Value;

        second.Current.Id.Should().Be(3);
        second.LegacyCutoverUtc.Should().Be(T0);
        second.LegacyVerifyUntilUtc.Should().Be(T0.AddDays(20), "a later promote does not shorten what key 1 issued");
        second.Find(2)!.DemotedAtUtc.Should().Be(T0.AddDays(2));
        second.Accepted(CredentialPurpose.Invitation, T0.AddDays(3)).Select(d => (d.Key.Id, d.IsLegacy))
            .Should().Equal((3, false), (2, false), (1, true));
    }

    [Fact]
    public void Emergency_OverAFullRing_LeavesOnlyTheNewKey_ExceptForWebhookKeys()
    {
        // Key 1 in its legacy window, key 2 previous in its window, key 3 current, key 4 staged.
        var full = Staged().Promote(T0, T0.AddDays(20)).Value
            .Stage(Material(200), T0.AddDays(1)).Value
            .Promote(T0.AddDays(2), T0.AddDays(15)).Value
            .Stage(Material(300), T0.AddDays(3)).Value;
        full.Keys.Select(k => k.Id).Should().BeEquivalentTo([1, 2, 3, 4]);
        var leak = T0.AddDays(4);

        var ring = full.PromoteEmergency(Material(400), leak).Value;

        ring.Current.Id.Should().Be(5);
        ring.Next.Should().BeNull("an emergency never promotes the staged key: a leaked file leaks it too");
        ring.Keys.Where(k => k.Id != 5).Should().OnlyContain(k => k.Compromised && k.VerifyUntilUtc == leak);
        foreach (var purpose in Bounded)
        {
            ring.Accepted(purpose, leak).Select(d => d.Key.Id).Should().Equal([5], $"{purpose} closes at once");
        }

        ring.Accepted(CredentialPurpose.WebhookKey, leak.AddDays(1)).Select(d => (d.Key.Id, d.IsLegacy))
            .Should().Equal((5, false), (3, false), (2, false), (1, true));
        ring.Accepted(CredentialPurpose.WebhookKey, T0.AddDays(91)).Select(d => d.Key.Id)
            .Should().Equal([5, 3, 2], "the legacy grace runs 90 days from the cutover, each previous key's from its demotion");
    }

    [Fact]
    public void Emergency_FromKey1_RecordsTheCutoverAndClosesTheLegacyFormNow()
    {
        var ring = KeyringTestRings.SingleKey().PromoteEmergency(Material(100), T0).Value;

        ring.Current.Id.Should().Be(2);
        ring.LegacyCutoverUtc.Should().Be(T0);
        ring.Accepted(CredentialPurpose.PasswordReset, T0).Select(d => d.Key.Id).Should().Equal([2]);
        ring.Accepted(CredentialPurpose.WebhookKey, T0.AddDays(89)).Should().Contain(d => d.IsLegacy);
    }

    [Fact]
    public void Emergency_WithMaterialTheRingHolds_IsRefused()
    {
        var result = Staged().PromoteEmergency(Material(100), T0);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
    }

    [Fact]
    public void DiscardNext_RemovesTheStagedKey_AndWithoutOneIsRefused()
    {
        var ring = Staged().DiscardNext().Value;

        ring.Next.Should().BeNull();
        ring.Keys.Select(k => k.Id).Should().Equal([1]);
        ring.DiscardNext().FirstError.Code.Should().Be("Secret.KeyringNoNextKey");
    }

    [Fact]
    public void Reinstating1_AfterTheCutover_IssuesTheLegacyFormAgain_AndKeepsKey2sSubkeysForTheirWindow()
    {
        var promoted = Staged().Promote(T0, T0.AddDays(20)).Value;
        var back = T0.AddDays(1);

        var ring = promoted.Reinstate(1, back, back.AddDays(10)).Value;

        ring.Current.Id.Should().Be(1);
        ring.Issuing.IsLegacy.Should().BeTrue();
        ring.Find(2)!.DemotedAtUtc.Should().Be(back);
        ring.Accepted(CredentialPurpose.RefreshToken, back.AddDays(1)).Select(d => (d.Key.Id, d.IsLegacy))
            .Should().Equal((1, true), (2, false));
        ring.Accepted(CredentialPurpose.RefreshToken, back.AddDays(11)).Select(d => d.Key.Id).Should().Equal([1]);
    }

    [Fact]
    public void APromoteAfterReinstating1_RecordsANewCutover_SoWhatKey1IssuedSinceStaysAccepted()
    {
        var reinstated = Staged().Promote(T0, T0.AddDays(8)).Value.Reinstate(1, T0.AddDays(1), T0.AddDays(9)).Value;
        var again = T0.AddDays(10);

        var ring = reinstated.Stage(Material(200), T0.AddDays(9)).Value.Promote(again, again.AddDays(7)).Value;

        ring.LegacyCutoverUtc.Should().Be(again);
        ring.Accepted(CredentialPurpose.Invitation, again.AddDays(5)).Should().Contain(d => d.IsLegacy,
            "an invitation key 1 sent during the reinstatement lives seven days past this cutover");
    }

    [Fact]
    public void Reinstate_IsRefusedFor_ACompromisedKey_AKeyPastItsWindow_AndTheCurrentKey()
    {
        var promoted = Staged().Promote(T0, T0.AddDays(7)).Value;
        var emergency = promoted.PromoteEmergency(Material(200), T0.AddDays(1)).Value;

        emergency.Reinstate(2, T0.AddDays(2), T0.AddDays(9)).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");
        promoted.Reinstate(1, T0.AddDays(8), T0.AddDays(15)).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");
        promoted.Reinstate(2, T0.AddDays(1), T0.AddDays(8)).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");
        promoted.Reinstate(9, T0.AddDays(1), T0.AddDays(8)).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");
    }

    [Fact]
    public void Retire_RemovesAPreviousKey_ButNeverTheCurrentOrTheStagedKey()
    {
        var ring = Staged().Promote(T0, T0.AddDays(7)).Value.Stage(Material(200), T0.AddDays(1)).Value;

        ring.Retire(2).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");
        ring.Retire(3).FirstError.Code.Should().Be("Secret.KeyringKeyNotFound");

        var retired = ring.Retire(1).Value;
        retired.Keys.Select(k => k.Id).Should().BeEquivalentTo([2, 3]);
        retired.Accepted(CredentialPurpose.RefreshToken, T0.AddDays(2)).Should().NotContain(d => d.IsLegacy);
    }

    [Theory]
    [InlineData(null, null, 7)]   // nothing live: the shortest window
    [InlineData(3, 2, 7)]         // short-lived credentials: still the shortest window
    [InlineData(12, 20, 20)]      // the longest live credential sizes it
    [InlineData(365, 90, 30)]     // capped at 30 days (D-S11B-2)
    public void TheWindowOfADemotedKey_IsSizedByTheLatestLiveCredential(int? refreshDays, int? ssoDays, int expectedDays)
    {
        var until = PurposeLimitedOverlap.DemotedKeyVerifyUntil(
            T0,
            refreshDays is { } r ? T0.AddDays(r) : null,
            ssoDays is { } s ? T0.AddDays(s) : null);

        until.Should().Be(T0.AddDays(expectedDays).AddSeconds(300));
    }

    [Fact]
    public void AcceptedUntil_AgreesWithAccepted_ForEveryKeyAndPurpose()
    {
        var ring = Staged().Promote(T0, T0.AddDays(20)).Value.Stage(Material(200), T0.AddDays(1)).Value;

        foreach (var purpose in CredentialPurposePolicy.Purposes)
        {
            foreach (var at in new[] { T0.AddMinutes(1), T0.AddDays(2), T0.AddDays(25), T0.AddDays(95) })
            {
                var accepted = ring.Accepted(purpose, at).Select(d => d.Key.Id).ToHashSet();
                foreach (var key in ring.Keys)
                {
                    var until = ring.AcceptedUntil(key, purpose);
                    accepted.Contains(key.Id).Should().Be(until is { } u && at <= u, $"key {key.Id}, {purpose}, {at:u}");
                }
            }
        }
    }
}
