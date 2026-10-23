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
    [InlineData(CredentialPurpose.RefreshToken, "AuthSystem/credential-hash/RefreshToken/v1")]
    [InlineData(CredentialPurpose.IdpSession, "AuthSystem/credential-hash/IdpSession/v1")]
    [InlineData(CredentialPurpose.AuthorizationCode, "AuthSystem/credential-hash/AuthorizationCode/v1")]
    [InlineData(CredentialPurpose.AuthorizationRequestHandle, "AuthSystem/credential-hash/AuthorizationRequestHandle/v1")]
    [InlineData(CredentialPurpose.PasswordReset, "AuthSystem/credential-hash/PasswordReset/v1")]
    [InlineData(CredentialPurpose.Invitation, "AuthSystem/credential-hash/Invitation/v1")]
    [InlineData(CredentialPurpose.TwoFactorChallenge, "AuthSystem/credential-hash/TwoFactorChallenge/v1")]
    [InlineData(CredentialPurpose.ExternalNonce, "AuthSystem/credential-hash/ExternalNonce/v1")]
    [InlineData(CredentialPurpose.StepUpTicket, "AuthSystem/credential-hash/StepUpTicket/v1")]
    [InlineData(CredentialPurpose.Otp, "AuthSystem/credential-hash/Otp/v1")]
    [InlineData(CredentialPurpose.TwoFactorBind, "AuthSystem/credential-hash/TwoFactorBind/v1")]
    [InlineData(CredentialPurpose.PendingRegistrationHandle, "AuthSystem/credential-hash/PendingRegistrationHandle/v1")]
    [InlineData(CredentialPurpose.WebhookKey, "AuthSystem/credential-hash/WebhookKey/v1")]
    [InlineData(CredentialPurpose.TwoFactorEmailCode, "AuthSystem/credential-hash/TwoFactorEmailCode/v1")]
    public void TheLabel_IsPinned(CredentialPurpose purpose, string label)
    {
        // Literals, not the member name: renaming a member must not change a subkey, and
        // changing a label changes every subkey fingerprint of that purpose.
        CredentialPurposePolicy.For(purpose).DerivationLabel.Should().Be(label);
    }

    [Fact]
    public void EveryPurpose_HasAPinnedLabel()
    {
        typeof(PurposeSubkeyDerivationTests).GetMethod(nameof(TheLabel_IsPinned))!
            .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
            .Should().HaveCount(Enum.GetValues<CredentialPurpose>().Length);
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
    /// Each limit covers the longest lifetime the console accepts for its credential (and
    /// the code's own clamps where they exist), so a rotation never drops a credential
    /// that is still valid (fact 3; E10). A settings-file value above the console's
    /// maximum is not clamped for a few of them (deviation row, S11).
    /// </summary>
    [Fact]
    public void PurposeLimits_CoverTheLongestLifetimeTheConsoleAccepts()
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

        // A challenge is created with the default lifetime and passes no other, then each
        // emailed code moves its expiry to the send plus the code's lifetime, at most
        // MaxEmailCodeSends times, each send made while the challenge is still live (S24).
        var longestEmailCode = Math.Max(Max("TwoFactor", "EmailCodeLifetimeMinutes"), TwoFactorSettings.MaxEmailCodeLifetimeMinutes);
        Limit(CredentialPurpose.TwoFactorChallenge).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(
            TwoFactorChallenge.DefaultLifetimeMinutes + TwoFactorChallenge.MaxEmailCodeSends * longestEmailCode));
        Limit(CredentialPurpose.ExternalNonce).Should().BeGreaterThanOrEqualTo(ExternalNonceCookie.Lifetime);
        Limit(CredentialPurpose.Invitation).Should().BeGreaterThanOrEqualTo(
            TimeSpan.FromDays(OrganizationInvitation.DefaultExpirationDays));
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
