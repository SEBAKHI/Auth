using Auth.Application.Security.Keyring;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// S11 SEC-2: a ring no transition can produce is refused when it is built, at boot and
/// before every save: one whose current key is marked compromised, and one holding the
/// same material under two ids (which would make key 1 a key-derivation key under
/// another id, RV-P4-3 by material).
/// </summary>
public class HmacKeyringCreateTests
{
    private static readonly DateTime Cutover = KeyringTestRings.Cutover;

    [Fact]
    public void Create_RefusesACompromisedCurrentKey()
    {
        var result = HmacKeyring.Create(
            [
                new HmacKey(1, KeyringTestRings.Key1Material, demotedAtUtc: Cutover),
                new HmacKey(2, KeyringTestRings.Key2Material, compromised: true)
            ],
            currentKeyId: 2,
            nextKeyId: null,
            legacyCutoverUtc: Cutover,
            legacyVerifyUntilUtc: null);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Secret.InvalidKeyMaterial");
        result.FirstError.Description.Should().Contain("compromised");
    }

    [Fact]
    public void Create_RefusesTheSameMaterialUnderTwoIds()
    {
        var result = HmacKeyring.Create(
            [
                new HmacKey(1, KeyringTestRings.Key1Material, demotedAtUtc: Cutover),
                new HmacKey(2, KeyringTestRings.Key1Material)
            ],
            currentKeyId: 2,
            nextKeyId: null,
            legacyCutoverUtc: Cutover,
            legacyVerifyUntilUtc: null);

        result.IsError.Should().BeTrue();
        result.FirstError.Description.Should().Contain("keys 1 and 2");
    }

    [Fact]
    public void Create_AcceptsACompromisedPreviousKey_WhichEveryEmergencyLeaves()
    {
        var result = HmacKeyring.Create(
            [
                new HmacKey(1, KeyringTestRings.Key1Material, demotedAtUtc: Cutover, compromised: true),
                new HmacKey(2, KeyringTestRings.Key2Material)
            ],
            currentKeyId: 2,
            nextKeyId: null,
            legacyCutoverUtc: Cutover,
            legacyVerifyUntilUtc: Cutover);

        result.IsError.Should().BeFalse();
    }
}
