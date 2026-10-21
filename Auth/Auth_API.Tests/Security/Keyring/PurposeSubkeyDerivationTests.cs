using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Security.Keyring;
using Auth.Application.SystemSettings;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth_API.Common;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 TS1: once the keyring issues under purpose subkeys, each purpose has its own
/// key — SP 800-108r1 KBKDF in counter mode over HMAC-SHA256 — so a fingerprint of
/// one kind of credential is never a fingerprint of another, and never key 1's raw
/// form (RV-P4-3).
/// </summary>
public class PurposeSubkeyDerivationTests
{
    private const string Value = "the-same-presented-value";

    public static TheoryData<CredentialPurpose> AllPurposes()
    {
        var data = new TheoryData<CredentialPurpose>();
        foreach (var purpose in Enum.GetValues<CredentialPurpose>())
        {
            data.Add(purpose);
        }

        return data;
    }

    [Fact]
    public void EveryPurpose_HasARule_AndTheTableHasNoOther()
    {
        CredentialPurposePolicy.Purposes.Should().BeEquivalentTo(Enum.GetValues<CredentialPurpose>());
    }

    [Theory]
    [MemberData(nameof(AllPurposes))]
    public void TheLabel_IsPinned(CredentialPurpose purpose)
    {
        // Written per member, never derived from its name: renaming a member must not
        // change a subkey, and changing a label changes every subkey fingerprint.
        CredentialPurposePolicy.For(purpose).DerivationLabel.Should().Be($"AuthSystem/credential-hash/{purpose}/v1");
    }

    [Theory]
    [MemberData(nameof(AllPurposes))]
    public void Hash_UnderKey2_IsHmacUnderTheManuallyDerivedKbkdfSubkey(CredentialPurpose purpose)
    {
        var hasher = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(KeyringTestRings.Cutover));

        var subkey = ManualKbkdf(KeyringTestRings.Key2Material, CredentialPurposePolicy.For(purpose).DerivationLabel);
        var mac = Convert.ToBase64String(HMACSHA256.HashData(subkey, Encoding.UTF8.GetBytes(Value)));
        var expected = purpose == CredentialPurpose.WebhookKey ? "hk2$" + mac : mac;

        hasher.Hash(purpose, Value).Should().Be(expected);
    }

    [Fact]
    public void EveryPurpose_GetsADistinctFingerprint_OfTheSameValue_AndNoneIsTheLegacyForm()
    {
        var hasher = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(KeyringTestRings.Cutover));
        var legacy = Convert.ToBase64String(HMACSHA256.HashData(KeyringTestRings.Key1Material, Encoding.UTF8.GetBytes(Value)));

        var macs = Enum.GetValues<CredentialPurpose>()
            .Select(purpose => hasher.Hash(purpose, Value))
            .Select(stored => stored.Contains('$') ? stored[(stored.IndexOf('$') + 1)..] : stored)
            .ToList();

        macs.Should().OnlyHaveUniqueItems("each purpose has its own subkey");
        macs.Should().NotContain(legacy, "key 2 issues subkeys, never a raw HMAC");
    }

    [Fact]
    public void Key1_IsNeverAKeyDerivationKey()
    {
        // RV-P4-3: key 1's material stays a raw HMAC key; deriving from it would make one
        // key both a KDF key and an HMAC key (SP 800-108r1).
        var key1 = new HmacKey(1, KeyringTestRings.Key1Material);

        var derive = () => HmacDerivation.Subkey(key1);

        derive.Should().Throw<InvalidOperationException>();
        HmacDerivation.Legacy(key1).IsLegacy.Should().BeTrue();
        ((Action)(() => HmacDerivation.Legacy(new HmacKey(2, KeyringTestRings.Key2Material))))
            .Should().Throw<InvalidOperationException>("only key 1 computes the legacy form");
    }

    [Fact]
    public void ARingWhoseCurrentKeyIsNot1_RequiresACutover()
    {
        // The state no transition writes: a promote records the cutover with the new key.
        var ring = HmacKeyring.Create(
            [new HmacKey(1, KeyringTestRings.Key1Material), new HmacKey(2, KeyringTestRings.Key2Material)],
            currentKeyId: 2, nextKeyId: null, legacyCutoverUtc: null, legacyVerifyUntilUtc: null);

        ring.IsError.Should().BeTrue();
        ring.FirstError.Description.Should().Contain("no cutover");
    }

    /// <summary>
    /// Each limit covers the longest lifetime the code lets its credential have, so a
    /// rotation never drops a credential that is still valid (fact 3; E10).
    /// </summary>
    [Fact]
    public void PurposeLimits_CoverTheLongestConfigurableLifetime()
    {
        static TimeSpan Limit(CredentialPurpose purpose) =>
            ((PurposeLimitedOverlap)CredentialPurposePolicy.For(purpose).Overlap).Limit;

        static long Max(string section, string field) =>
            SystemSettingsRegistry.TryGetField(SystemSettingsRegistry.TryGet(section)!, field)!.Max!.Value;

        var longest = new IdentityProviderSettings
        {
            StepUpTicketLifetimeMinutes = int.MaxValue,
            AuthorizationRequestLifetimeMinutes = int.MaxValue
        };

        Limit(CredentialPurpose.StepUpTicket).Should().BeGreaterThanOrEqualTo(longest.StepUpTicketLifetime);
        Limit(CredentialPurpose.AuthorizationRequestHandle).Should().BeGreaterThanOrEqualTo(longest.AuthorizationRequestLifetime);
        Limit(CredentialPurpose.AuthorizationCode).Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromSeconds(Max("IdentityProvider", "AuthorizationCodeLifetimeSeconds")));
        Limit(CredentialPurpose.PasswordReset).Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromMinutes(Max("Email", "ResetTokenExpirationMinutes")));

        var longestOtp = TimeSpan.FromMinutes(Math.Max(
            Max("Email", "OtpExpirationMinutes"), Max("AccountDeletion", "OtpExpirationMinutes")));
        Limit(CredentialPurpose.Otp).Should().BeGreaterThanOrEqualTo(longestOtp);
        Limit(CredentialPurpose.TwoFactorBind).Should().BeGreaterThanOrEqualTo(longestOtp);
        Limit(CredentialPurpose.PendingRegistrationHandle).Should().BeGreaterThanOrEqualTo(longestOtp);

        Limit(CredentialPurpose.TwoFactorChallenge).Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromMinutes(TwoFactorChallenge.DefaultLifetimeMinutes));
        Limit(CredentialPurpose.ExternalNonce).Should().BeGreaterThanOrEqualTo(ExternalNonceCookie.Lifetime);
        Limit(CredentialPurpose.Invitation).Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(7));
    }

    /// <summary>
    /// SP 800-108r1 §4.1, counter mode, written out by hand: K(1) = PRF(K_in,
    /// [1]_32 || Label || 0x00 || Context || [L]_32), with L = 256 and an empty context.
    /// One block of HMAC-SHA256 output is exactly the 256 bits asked for.
    /// </summary>
    private static byte[] ManualKbkdf(byte[] key, string label)
    {
        var labelBytes = Encoding.UTF8.GetBytes(label);
        var input = new byte[4 + labelBytes.Length + 1 + 4];
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(0, 4), 1);
        labelBytes.CopyTo(input, 4);
        input[4 + labelBytes.Length] = 0x00;
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(input.Length - 4), 256);
        return HMACSHA256.HashData(key, input);
    }
}
