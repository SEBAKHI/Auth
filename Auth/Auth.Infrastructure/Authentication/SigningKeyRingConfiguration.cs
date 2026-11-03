using System.Globalization;
using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Auth.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Loads the signing keyring at start, once, inside the "key material validation" step (S12,
/// KR8), refusing anything it cannot trust: a bad ring stops the process at boot with a reason,
/// instead of a token signed or refused under the wrong key.
/// </summary>
/// <remarks>
/// <para>
/// The key's source is the loader's (<see cref="SigningKeyLoader.Read"/>): only a key the
/// encrypted secrets file supplies has a ring. A key from <c>Jwt:PrivateKeyPath</c>,
/// <c>Jwt:PrivateKeyEncrypted</c>, or a PlainText settings file is one key under
/// <c>Jwt:KeyId</c>, and <c>Keyring:Rsa</c> is not read.
/// </para>
/// <para>
/// Sources of the ring, mapped from the secrets file by the secret provider:
/// <c>Keyring:Rsa:CurrentKid</c>, <c>Keyring:Rsa:NextKid</c>, and per key
/// <c>Keys:{kid}:PrivateKeyPem|PublicKeyPem|CreatedAtUtc|PublishedAtUtc|VerifyUntilUtc|Compromised</c>.
/// No ring in the file means the deployment never rotated: the single key is the current key,
/// named by <c>Jwt:KeyId</c>, and nothing is written (KR2).
/// </para>
/// </remarks>
public static class SigningKeyRingConfiguration
{
    /// <summary>The configuration section of the ring.</summary>
    public const string SectionName = "Keyring:Rsa";

    /// <summary>Loads the keys this process signs and verifies under, or throws with the reason it cannot.</summary>
    /// <param name="configuration">The process's configuration.</param>
    /// <param name="settings">The JWT settings, for the key's sources and <c>Jwt:KeyId</c>.</param>
    /// <param name="dataProtectionProvider">Decrypts <c>Jwt:PrivateKeyEncrypted</c>.</param>
    /// <param name="plainText">Whether secrets are kept in PlainText mode.</param>
    /// <exception cref="InvalidOperationException">The key cannot sign, or the ring is malformed, comes from another source, or disagrees with the single key.</exception>
    public static SigningKeyRing Load(
        IConfiguration configuration,
        JwtSettings settings,
        IDataProtectionProvider? dataProtectionProvider,
        bool plainText)
    {
        var winner = SigningKeyLoader.Read(settings, dataProtectionProvider);

        // Jwt:KeyId is read here once: every later reader of the migrated key's name reads it
        // from the source, so an edit of the setting after the start renames nothing.
        var source = new SigningKeySource(winner.Source, plainText, RsaKeyring.MigratedKidFrom(settings.KeyId));

        if (!source.HoldsRing)
        {
            // The process's one key for its whole life: nothing disposes it.
            var rsa = RSA.Create();
            rsa.ImportFromPem(winner.PrivateKeyPem);
            return SigningKeyRing.Single(rsa, source.MigratedKid, source);
        }

        var foreign = SecretFileOnlySection.ForeignValues(configuration, SectionName)
            ?? throw new InvalidOperationException(
                "The signing keyring's configuration sources cannot be inspected, so the ring cannot be shown to come only from the secrets file.");
        if (foreign.Count > 0)
        {
            throw new InvalidOperationException(
                $"The signing keyring may only come from the encrypted secrets file, but [{string.Join(", ", foreign)}] " +
                "set it from another source. Remove those values (an environment variable, a settings file), then start again.");
        }

        var section = configuration.GetSection(SectionName);
        var entries = ReadEntries(section);

        // No ring in the file: the one key is named by Jwt:KeyId, so a name the ring refuses is
        // that setting's fault, and no copy of the secrets file would help.
        if (entries.Count == 0 && !RsaKeyring.IsValidKid(source.MigratedKid))
        {
            throw new InvalidOperationException(
                $"Refusing to start: Jwt:KeyId '{source.MigratedKid}' cannot name the signing key, because a kid has at most " +
                $"{RsaKeyring.MaxKidLength} characters and holds no ':'. Set Jwt:KeyId to the kid the JWKS published until now; " +
                "if that kid breaks the rule too, choose a new one that follows it, which changes the published kid. Then start again.");
        }

        var ring = RsaKeyringFile.FromEntries(
            entries,
            Blank(section["CurrentKid"]),
            Blank(section["NextKid"]),
            winner.PrivateKeyPem,
            removed: [],
            source.MigratedKid);

        if (ring.IsError)
        {
            throw new InvalidOperationException(
                $"Refusing to start: the signing keyring cannot be used. {string.Join(" ", ring.Errors.Select(error => error.Description))} " +
                "Restore the most recent copy of the secrets file taken after the last keyring change, then start again.");
        }

        return SigningKeyRing.Load(ring.Value, source);
    }

    private static Dictionary<string, SigningKeyEntry> ReadEntries(IConfigurationSection section)
    {
        var entries = new Dictionary<string, SigningKeyEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in section.GetSection("Keys").GetChildren())
        {
            // A key whose every value is empty is no key: the empty placeholder that settings
            // files use for "not set" must not stop the boot.
            if (!key.AsEnumerable().Any(pair => !string.IsNullOrWhiteSpace(pair.Value)))
            {
                continue;
            }

            entries[key.Key] = new SigningKeyEntry
            {
                PrivateKeyPem = Blank(key["PrivateKeyPem"]),
                PublicKeyPem = Blank(key["PublicKeyPem"]),
                CreatedAtUtc = ReadDate(key, "CreatedAtUtc"),
                PublishedAtUtc = ReadDate(key, "PublishedAtUtc"),
                VerifyUntilUtc = ReadDate(key, "VerifyUntilUtc"),
                Compromised = string.Equals(key["Compromised"], "true", StringComparison.OrdinalIgnoreCase)
            };
        }

        return entries;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : throw new InvalidOperationException($"The signing keyring's {name} '{raw}' is not a date.");
    }
}
