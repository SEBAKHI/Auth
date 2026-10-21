using Auth.Application.Security.Keyring;

namespace Auth_API.Tests.Security.Keyring;

/// <summary>
/// The key material and rings the keyring tests share. Key 1's material is the
/// one the golden values in <see cref="LegacyDerivationCompatibilityTests"/> were
/// captured under, with the classes this keyring replaced.
/// </summary>
internal static class KeyringTestRings
{
    /// <summary>The instant of the cutover in every rotated ring.</summary>
    public static readonly DateTime Cutover = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Bytes 0..31: the key the golden values were captured under.</summary>
    public static byte[] Key1Material => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    /// <summary>Bytes 32..63.</summary>
    public static byte[] Key2Material => Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();

    /// <summary>Bytes 64..95.</summary>
    public static byte[] Key3Material => Enumerable.Range(64, 32).Select(i => (byte)i).ToArray();

    /// <summary>A deployment that never rotated: key 1, current, nothing else.</summary>
    public static HmacKeyring SingleKey() => HmacKeyring.FromSingleKey(Key1Material);

    /// <summary>
    /// After the first promote: key 2 issues purpose subkeys; key 1 verifies the
    /// legacy form until its window closes.
    /// </summary>
    public static HmacKeyring Rotated(
        bool key1Compromised = false,
        DateTime? key1VerifyUntil = null,
        DateTime? legacyVerifyUntil = null) =>
        HmacKeyring.Create(
            [
                new HmacKey(1, Key1Material, demotedAtUtc: Cutover, verifyUntilUtc: key1VerifyUntil, compromised: key1Compromised),
                new HmacKey(2, Key2Material, createdAtUtc: Cutover.AddDays(-1))
            ],
            currentKeyId: 2,
            nextKeyId: null,
            legacyCutoverUtc: Cutover,
            legacyVerifyUntilUtc: legacyVerifyUntil).Value;
}
