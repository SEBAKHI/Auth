using Auth.Application.Security.Keyring;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 (KR2): a stored webhook fingerprint names the key that computed it,
/// <c>hk{id}$</c> + base64, so a key is retired only once no live webhook key needs
/// it. The legacy form stays bare, so every key stored before S11 reads as key 1.
/// </summary>
public class WebhookKeyHashFormatTests
{
    private const string Mac = "S2HEKqunY1YOsJmIBLV8BjHWXWzdVkpq/dDghqx3M20=";

    [Theory]
    [InlineData("hk2$" + Mac, 2)]
    [InlineData("hk17$" + Mac, 17)]
    public void TryReadKeyId_APrefixedValue_GivesItsKey(string stored, int expected)
    {
        WebhookKeyHashFormat.TryReadKeyId(stored, out var keyId).Should().BeTrue();
        keyId.Should().Be(expected);
    }

    [Fact]
    public void TryReadKeyId_ABareValue_IsTheLegacyForm()
    {
        WebhookKeyHashFormat.TryReadKeyId(Mac, out var keyId).Should().BeTrue();
        keyId.Should().BeNull();
    }

    [Theory]
    [InlineData("hk$" + Mac)]
    [InlineData("hkx$" + Mac)]
    [InlineData("hk-1$" + Mac)]
    [InlineData("hk0$" + Mac)]
    [InlineData("hk99999999999$" + Mac)]
    [InlineData("xx2$" + Mac)]
    [InlineData("hk2$")]
    [InlineData("")]
    [InlineData(null)]
    public void TryReadKeyId_AMalformedValue_NamesNoKey_AndDoesNotThrow(string? stored)
    {
        var read = () => WebhookKeyHashFormat.TryReadKeyId(stored, out _);

        read.Should().NotThrow();
        WebhookKeyHashFormat.TryReadKeyId(stored, out var keyId).Should().BeFalse();
        keyId.Should().BeNull();
    }

    [Fact]
    public void TheFormat_IsChosenFromThePurposeTable()
    {
        // Webhook keys carry the prefix; no other purpose does (their columns are sized
        // for the bare 44 characters, KR2).
        foreach (var purpose in Enum.GetValues<CredentialPurpose>())
        {
            CredentialPurposePolicy.For(purpose).Format.Should().BeOfType(
                purpose == CredentialPurpose.WebhookKey ? typeof(WebhookKeyHashFormat) : typeof(PlainCredentialHashFormat));
        }
    }

    [Fact]
    public void AWebhookKeyIssuedUnderKey2_IsPrefixed_AndFoundWhileTheLegacyOneStillIs()
    {
        var single = TestCredentialHashers.Over(KeyringTestRings.SingleKey());
        var rotated = TestCredentialHashers.Over(KeyringTestRings.Rotated(), new FixedTimeProvider(KeyringTestRings.Cutover.AddDays(1)));

        var legacy = single.Hash(CredentialPurpose.WebhookKey, "wk_live_sample");
        var issued = rotated.Hash(CredentialPurpose.WebhookKey, "wk_live_sample");

        legacy.Should().Be(Mac);
        issued.Should().StartWith("hk2$");
        WebhookKeyHashFormat.TryReadKeyId(issued, out var keyId).Should().BeTrue();
        keyId.Should().Be(2);
        rotated.Candidates(CredentialPurpose.WebhookKey, "wk_live_sample").Values.Should().Equal(issued, legacy);
    }
}
