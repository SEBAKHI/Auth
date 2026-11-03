using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;

namespace Auth_API.Tests.SecretManagement;

/// <summary>
/// S12 PR B (T4, stop condition 4): one step-up binding for two rings. The HMAC digest stays
/// byte for byte what S11 shipped, so a confirmation raised before the deploy still spends after
/// it; the signing ring's digest has a prefix of its own.
/// </summary>
public class KeyringStepUpDigestTests
{
    // Computed with a6eb164a's KeyringStepUp, whose format is
    // SHA-256 hex of "keyring:hmac/{action}/{target}\n{value}".
    [Theory]
    [InlineData(KeyringAction.Promote, "2@2026-01-02T03:04:05.0000000Z", null, "3b6ed16418a78767d2934f86366d9b44dd7bca99862a5fcdb5697e70ab410c29")]
    [InlineData(KeyringAction.StageImport, "1", "AAAA", "c548eb3f564182f042225e3efedc5d4a136b24197aadc792d94ba2cc5b279c33")]
    [InlineData(KeyringAction.Retire, "3@", null, "938684aa9e8a8c30e1aabf7d1f7ae932d8ae61baaf0cccfae1630c91fbc8cad6")]
    public void TheHmacDigest_IsByteIdenticalToTheShippedOne(KeyringAction action, string target, string? value, string shipped)
    {
        KeyringStepUp.Digest(action, target, value).Should().Be(shipped);
    }

    [Fact]
    public void TheSigningDigest_HasItsOwnRingPrefix()
    {
        // SHA-256 hex of "keyring:rsa/Promote/abc\n".
        KeyringStepUp.SigningDigest(KeyringAction.Promote, "abc", null)
            .Should().Be("5eeb6210365b7c71a0a9856d133eb2d67c38cbc23d434951413fb2717d3d7c43")
            .And.NotBe(KeyringStepUp.Digest(KeyringAction.Promote, "abc", null));
    }

    [Theory]
    [InlineData(KeyringAction.Stage, SecretOperation.GenerateRsaKey)]
    [InlineData(KeyringAction.StageImport, SecretOperation.ImportRsaKey)]
    [InlineData(KeyringAction.Promote, SecretOperation.GenerateRsaKey)]
    [InlineData(KeyringAction.PromoteEmergency, SecretOperation.GenerateRsaKey)]
    [InlineData(KeyringAction.DiscardNext, SecretOperation.GenerateRsaKey)]
    [InlineData(KeyringAction.Retire, SecretOperation.GenerateRsaKey)]
    public void EverySigningAction_IsConfirmedByAnRsaOperation_WithNoNewMember(KeyringAction action, SecretOperation operation)
    {
        KeyringStepUp.SigningOperationOf(action).Should().Be(operation);
        KeyringStepUp.Confirms(action, operation).Should().BeTrue();
    }

    [Fact]
    public void AReinstate_IsAnHmacActionOnly()
    {
        KeyringStepUp.Confirms(KeyringAction.Reinstate, SecretOperation.GenerateRsaKey).Should().BeFalse();
    }
}
