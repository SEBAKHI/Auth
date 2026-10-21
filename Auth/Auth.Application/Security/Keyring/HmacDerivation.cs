namespace Auth.Application.Security.Keyring;

/// <summary>
/// How one key computes a credential's fingerprint: the raw key (the legacy form,
/// byte for byte what the platform stored before the keyring) or the key's
/// purpose subkey.
/// </summary>
public sealed class HmacDerivation
{
    private HmacDerivation(HmacKey key, bool isLegacy)
    {
        Key = key;
        IsLegacy = isLegacy;
    }

    /// <summary>The key the fingerprint is computed under.</summary>
    public HmacKey Key { get; }

    /// <summary>
    /// True for the legacy form: HMAC-SHA256 under key 1's raw material, with no
    /// purpose separation beyond what the caller's message carries.
    /// </summary>
    public bool IsLegacy { get; }

    /// <summary>The legacy form. Only key 1 ever computes it.</summary>
    public static HmacDerivation Legacy(HmacKey key)
    {
        if (key.Id != HmacKeyring.LegacyKeyId)
        {
            throw new InvalidOperationException(
                $"Only key {HmacKeyring.LegacyKeyId} computes the legacy form; key {key.Id} never does.");
        }

        return new HmacDerivation(key, isLegacy: true);
    }

    /// <summary>
    /// The purpose-subkey form. Never key 1: its material stays a raw HMAC key for
    /// verifying legacy values and is never also a key-derivation key
    /// (SP 800-108r1; RV-P4-3).
    /// </summary>
    public static HmacDerivation Subkey(HmacKey key)
    {
        if (key.Id == HmacKeyring.LegacyKeyId)
        {
            throw new InvalidOperationException(
                $"Purpose subkeys are never derived from key {HmacKeyring.LegacyKeyId}.");
        }

        return new HmacDerivation(key, isLegacy: false);
    }
}
