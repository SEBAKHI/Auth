using System.Globalization;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// The stored shape of a fingerprint: how a MAC and the derivation it came from
/// are written into the column.
/// </summary>
public interface ICredentialHashFormat
{
    /// <summary>Writes <paramref name="mac"/> (base64) as it is stored for <paramref name="derivation"/>.</summary>
    string Write(HmacDerivation derivation, string mac);
}

/// <summary>
/// The bare base64 MAC, 44 characters ending in '=', whatever key computed it
/// (KR2). Every bounded-lifetime credential is stored this way: their columns and
/// stored procedures are sized for it, and a value that ages out within the
/// overlap window needs no key id to be found.
/// </summary>
public sealed class PlainCredentialHashFormat : ICredentialHashFormat
{
    public string Write(HmacDerivation derivation, string mac) => mac;
}

/// <summary>
/// Webhook keys live until an administrator rotates them, so a stored webhook
/// fingerprint names the key that computed it: <c>hk{id}$</c> + base64 for a
/// purpose subkey, the bare base64 for the legacy form. '$' is not a base64
/// character, so the two forms can never be confused (the same reasoning as the
/// OTP hasher's argon2 prefix). The id is what lets a key be retired only once
/// no live webhook key still needs it.
/// </summary>
public sealed class WebhookKeyHashFormat : ICredentialHashFormat
{
    /// <summary>The prefix before the key id.</summary>
    public const string Prefix = "hk";

    /// <summary>The character that ends the key id; never part of base64.</summary>
    public const char Separator = '$';

    public string Write(HmacDerivation derivation, string mac) =>
        derivation.IsLegacy
            ? mac
            : string.Concat(Prefix, derivation.Key.Id.ToString(CultureInfo.InvariantCulture), Separator.ToString(), mac);

    /// <summary>
    /// Reads which key computed a stored webhook fingerprint. True with a null id
    /// for the legacy form; true with the id for <c>hk{id}$…</c>; false, without
    /// throwing, for anything else (a malformed value names no key).
    /// </summary>
    public static bool TryReadKeyId(string? stored, out int? keyId)
    {
        keyId = null;
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var separatorAt = stored.IndexOf(Separator);
        if (separatorAt < 0)
        {
            // No '$' at all: a bare base64 value, the legacy form.
            return true;
        }

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal) || separatorAt == stored.Length - 1)
        {
            return false;
        }

        var digits = stored.AsSpan(Prefix.Length, separatorAt - Prefix.Length);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
        {
            return false;
        }

        keyId = id;
        return true;
    }
}
