using System.Globalization;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Security.Keyring;
using Auth.Shared.Configuration;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Loads the HMAC keyring from configuration, refusing anything it cannot trust.
/// Runs once, at startup, inside the "key material validation" step (KR8), so a
/// bad key stops the process at boot with a reason instead of failing the first
/// request that hashes a credential.
/// </summary>
/// <remarks>
/// Sources:
/// <list type="bullet">
/// <item><c>Jwt:RefreshTokenHmacKeyPlain</c> — the single key every deployment has.
/// It always mirrors the ring's current key.</item>
/// <item><c>Keyring:Hmac:*</c> — the ring, mapped from the secrets file by the
/// secret provider: <c>CurrentKeyId</c>, <c>NextKeyId</c>, <c>LegacyCutoverUtc</c>,
/// <c>LegacyVerifyUntilUtc</c>, and per key
/// <c>Keys:{id}:Material|CreatedAtUtc|VerifyUntilUtc|DemotedAtUtc|Compromised</c>.</item>
/// </list>
/// No ring in the file means the deployment never rotated: the single key becomes
/// key 1 in memory, and nothing is written.
/// </remarks>
public static class HmacKeyringConfiguration
{
    /// <summary>The configuration key of the single (current) key.</summary>
    public const string ScalarKey = "Jwt:RefreshTokenHmacKeyPlain";

    /// <summary>The configuration section of the ring.</summary>
    public const string SectionName = "Keyring:Hmac";

    /// <summary>Builds the ring, or throws with every reason it cannot be trusted.</summary>
    /// <param name="configuration">The process's configuration.</param>
    /// <param name="secretFileOnly">
    /// True whenever the secrets live in the encrypted file (every storage mode but
    /// PlainText): the ring is then taken only from that file, and any
    /// <c>Keyring:Hmac</c> value another source supplies (an environment variable, a
    /// settings file) stops the boot (S11 SEC-1). Such a value would sit above the file,
    /// and with a second key in the ring it would decide which key verifies what.
    /// </param>
    /// <exception cref="InvalidOperationException">The keyring is missing, malformed, comes from another source, or disagrees with the single key.</exception>
    public static HmacKeyring Load(IConfiguration configuration, bool secretFileOnly = false)
    {
        if (secretFileOnly)
        {
            EnsureRingComesOnlyFromTheSecretFile(configuration);
        }

        var scalar = configuration[ScalarKey];
        if (string.IsNullOrWhiteSpace(scalar))
        {
            throw new InvalidOperationException(
                $"The refresh-token HMAC key ({ScalarKey}) is not configured; every opaque credential is fingerprinted under it.");
        }

        var scalarMaterial = Decode(scalar, $"the single key ({ScalarKey})");
        var section = configuration.GetSection(SectionName);

        // A key whose every value is empty is no key: the empty placeholder that
        // settings files use for "not set" must not stop the boot.
        var keySections = section.GetSection("Keys").GetChildren().Where(HasAnyValue).ToList();

        if (keySections.Count == 0)
        {
            if (ReadInt(section, "FileVersion") is >= SecretFileVersions.HmacKeyringBeyondKey1)
            {
                throw new InvalidOperationException(
                    "The secrets file had an HMAC keyring beyond key 1 and no longer has one: a build without the " +
                    "keyring saved the file and erased it. Starting on the single key would issue and verify under " +
                    "the wrong key. Restore the most recent copy of the secrets file taken after the last keyring " +
                    "change, then start again.");
            }

            var stray = section.AsEnumerable(makePathsRelative: true)
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => pair.Key)
                .ToList();
            if (stray.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The HMAC keyring has settings [{string.Join(", ", stray)}] but no keys. " +
                    "Restore the most recent copy of the secrets file taken after the last keyring change.");
            }

            return HmacKeyring.FromSingleKey(scalarMaterial);
        }

        var keys = keySections.Select(ReadKey).ToList();
        var currentKeyId = ReadInt(section, "CurrentKeyId")
            ?? throw new InvalidOperationException("The HMAC keyring has keys but no CurrentKeyId.");

        var ring = HmacKeyring.Create(
            keys,
            currentKeyId,
            ReadInt(section, "NextKeyId"),
            ReadDate(section, "LegacyCutoverUtc"),
            ReadDate(section, "LegacyVerifyUntilUtc"));

        if (ring.IsError)
        {
            throw new InvalidOperationException(string.Join(" ", ring.Errors.Select(error => error.Description)));
        }

        // Every save mirrors the current key into the single key, so the two can only
        // disagree after a hand edit, or a restore that mixed two copies. Starting
        // anyway would issue under one key while the other half of the fleet, or the
        // next restart, verifies under the other. (A save by a build without the
        // keyring erases the ring instead; FileVersion above catches that.)
        if (!ring.Value.Current.Material.SequenceEqual(scalarMaterial))
        {
            throw new InvalidOperationException(
                $"The refresh-token HMAC key ({ScalarKey}) is not the HMAC keyring's current key {currentKeyId}. " +
                "The secrets file was most likely saved by a build without the keyring. Restore the most recent " +
                "copy of the secrets file taken after the last keyring change, then start again.");
        }

        return ring.Value;
    }

    private static void EnsureRingComesOnlyFromTheSecretFile(IConfiguration configuration)
    {
        if (configuration is not IConfigurationRoot root)
        {
            throw new InvalidOperationException(
                "The HMAC keyring's configuration sources cannot be inspected, so the ring cannot be shown to come only from the secrets file.");
        }

        var foreign = root.Providers
            .Where(provider => provider is not DpapiSecretConfigurationProvider)
            .SelectMany(provider => ValuedKeys(provider, SectionName).Select(key => $"{key} ({provider})"))
            .ToList();

        if (foreign.Count > 0)
        {
            throw new InvalidOperationException(
                $"The HMAC keyring may only come from the encrypted secrets file, but [{string.Join(", ", foreign)}] " +
                "set it from another source. Remove those values (an environment variable, a settings file), then start again.");
        }
    }

    // Every key under parentPath that the provider itself holds a non-empty value for.
    private static IEnumerable<string> ValuedKeys(IConfigurationProvider provider, string parentPath)
    {
        foreach (var child in provider.GetChildKeys([], parentPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = ConfigurationPath.Combine(parentPath, child);
            if (provider.TryGet(path, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                yield return path;
            }

            foreach (var nested in ValuedKeys(provider, path))
            {
                yield return nested;
            }
        }
    }

    private static bool HasAnyValue(IConfigurationSection section) =>
        section.AsEnumerable().Any(pair => !string.IsNullOrWhiteSpace(pair.Value));

    private static HmacKey ReadKey(IConfigurationSection keySection)
    {
        if (!int.TryParse(keySection.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 1)
        {
            throw new InvalidOperationException($"The HMAC keyring has a key with the invalid id '{keySection.Key}'.");
        }

        var material = keySection["Material"];
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new InvalidOperationException($"HMAC key {id} has no material.");
        }

        return new HmacKey(
            id,
            Decode(material, $"HMAC key {id}"),
            ReadDate(keySection, "CreatedAtUtc"),
            ReadDate(keySection, "VerifyUntilUtc"),
            ReadDate(keySection, "DemotedAtUtc"),
            string.Equals(keySection["Compromised"], "true", StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] Decode(string base64, string what)
    {
        var valid = SecretKeyMaterial.ValidateHmacKey(base64);
        if (valid.IsError)
        {
            // The description names the rule broken, never the value.
            throw new InvalidOperationException($"{what} is invalid: {valid.FirstError.Description}");
        }

        return Convert.FromBase64String(base64);
    }

    private static int? ReadInt(IConfiguration section, string name)
    {
        var raw = section[name];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"The HMAC keyring's {name} '{raw}' is not a key id.");
    }

    private static DateTime? ReadDate(IConfiguration section, string name)
    {
        var raw = section[name];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var value)
            ? value
            : throw new InvalidOperationException($"The HMAC keyring's {name} '{raw}' is not a date.");
    }
}
