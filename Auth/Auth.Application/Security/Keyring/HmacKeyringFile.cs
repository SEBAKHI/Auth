using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// Reads the HMAC keyring from the decrypted secrets file and writes one back into it
/// (KR1). A transition reads the ring the FILE holds, never the one in configuration,
/// which is the ring the process started with and goes stale at the first transition.
/// </summary>
/// <remarks>
/// The rules mirror the startup step (HmacKeyringConfiguration.Load): no ring means the
/// single key is key 1; every key and the single key must be valid material; the single
/// key must be the current key's material. A test pins that the two readers agree.
/// </remarks>
public static class HmacKeyringFile
{
    /// <summary>The ring the file holds.</summary>
    /// <returns>
    /// The ring; <see cref="SecretErrors.KeyringUnavailable"/> when the file holds no HMAC
    /// key at all (the process takes it from another source); or the reason the file's
    /// ring cannot be trusted.
    /// </returns>
    public static ErrorOr<HmacKeyring> Read(SecretConfiguration secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var entries = secrets.HmacKeys ?? new Dictionary<int, HmacKeyEntry>();
        if (string.IsNullOrWhiteSpace(secrets.RefreshTokenHmacKey))
        {
            return SecretErrors.KeyringUnavailable;
        }

        var single = Decode(secrets.RefreshTokenHmacKey, "the single HMAC key");
        if (single.IsError)
        {
            return single.Errors;
        }

        if (entries.Count == 0)
        {
            return HmacKeyring.FromSingleKey(single.Value);
        }

        var keys = new List<HmacKey>();
        foreach (var (id, entry) in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Material))
            {
                return SecretErrors.InvalidKeyMaterial($"HMAC key {id} in the secrets file has no material.");
            }

            var material = Decode(entry.Material, $"HMAC key {id}");
            if (material.IsError)
            {
                return material.Errors;
            }

            if (id < 1)
            {
                return SecretErrors.InvalidKeyMaterial($"The secrets file holds an HMAC key with the invalid id {id}.");
            }

            keys.Add(new HmacKey(
                id,
                material.Value,
                Utc(entry.CreatedAtUtc),
                Utc(entry.VerifyUntilUtc),
                Utc(entry.DemotedAtUtc),
                entry.Compromised));
        }

        var ring = HmacKeyring.Create(
            keys,
            secrets.HmacCurrentKeyId,
            secrets.HmacNextKeyId > 0 ? secrets.HmacNextKeyId : null,
            Utc(secrets.HmacLegacyCutoverUtc),
            Utc(secrets.HmacLegacyVerifyUntilUtc));
        if (ring.IsError)
        {
            return ring.Errors;
        }

        if (!ring.Value.Current.Material.SequenceEqual(single.Value))
        {
            return SecretErrors.InvalidKeyMaterial(
                $"The single HMAC key is not the keyring's current key {ring.Value.Current.Id}. " +
                "Restore the most recent copy of the secrets file taken after the last keyring change.");
        }

        return ring.Value;
    }

    /// <summary>
    /// Writes <paramref name="ring"/> into <paramref name="secrets"/>, the single key
    /// included (it mirrors the current key, KR1). Nothing else in the file changes.
    /// </summary>
    public static void Write(HmacKeyring ring, SecretConfiguration secrets)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(secrets);

        secrets.HmacKeys = ring.Keys.ToDictionary(
            key => key.Id,
            key => new HmacKeyEntry
            {
                Material = Convert.ToBase64String(key.Material),
                CreatedAtUtc = key.CreatedAtUtc,
                VerifyUntilUtc = key.VerifyUntilUtc,
                DemotedAtUtc = key.DemotedAtUtc,
                Compromised = key.Compromised
            });
        secrets.HmacCurrentKeyId = ring.Current.Id;
        secrets.HmacNextKeyId = ring.Next?.Id ?? 0;
        secrets.HmacLegacyCutoverUtc = ring.LegacyCutoverUtc;
        secrets.HmacLegacyVerifyUntilUtc = ring.LegacyVerifyUntilUtc;
        secrets.RefreshTokenHmacKey = Convert.ToBase64String(ring.Current.Material);
    }

    /// <summary>
    /// Whether two rings would issue and verify the same: the same current and staged
    /// keys (id and material) and the same cutover. The file and the process disagree
    /// when another worker changed the file, or a swap after a save failed.
    /// </summary>
    public static bool SameIssuingState(HmacKeyring file, HmacKeyring process) =>
        SameKey(file.Current, process.Current) &&
        SameKey(file.Next, process.Next) &&
        file.LegacyCutoverUtc == process.LegacyCutoverUtc;

    /// <summary>Whether two keys are the same key: same id, same material.</summary>
    public static bool SameKey(HmacKey? a, HmacKey? b) =>
        a is null || b is null
            ? a is null && b is null
            : a.Id == b.Id && a.Material.SequenceEqual(b.Material);

    private static ErrorOr<byte[]> Decode(string base64, string what)
    {
        var valid = SecretKeyMaterial.ValidateHmacKey(base64);
        if (valid.IsError)
        {
            // The description names the rule broken, never the value.
            return SecretErrors.InvalidKeyMaterial($"{what} is invalid: {valid.FirstError.Description}");
        }

        return Convert.FromBase64String(base64);
    }

    // A value written with an offset deserializes as local time: convert it, never
    // relabel it, or the instant moves by the server's offset (as the provider does).
    private static DateTime? Utc(DateTime? value) =>
        value is not { } date ? null
        : date.Kind == DateTimeKind.Local ? date.ToUniversalTime()
        : DateTime.SpecifyKind(date, DateTimeKind.Utc);
}
