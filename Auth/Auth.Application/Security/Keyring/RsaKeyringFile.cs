using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// Reads the RSA signing keyring from the decrypted secrets file and writes one back into it
/// (S12, KR1). A transition reads the ring the FILE holds, never the one in configuration,
/// which is the ring the process started with and goes stale at the first transition.
/// </summary>
/// <remarks>
/// The startup step reads the same entries from configuration and builds the ring with
/// <see cref="FromEntries"/> too, so the two readers apply one set of rules: no ring means the
/// single key (<c>Jwt:PrivateKeyPem</c>) is the current key, named by <c>Jwt:KeyId</c>, and not
/// yet persisted; the current and the staged key must hold a usable private key; every other
/// key a public key that parses; and the single key must be the current key.
/// </remarks>
public static class RsaKeyringFile
{
    /// <summary>The ring the file holds, with what the file remembers of keys that left it.</summary>
    /// <param name="secrets">The decrypted secrets file.</param>
    /// <param name="migratedKid">The kid the single key is published under while the file holds no ring (<c>Jwt:KeyId</c>).</param>
    /// <returns>
    /// The ring; <see cref="SecretErrors.KeyringUnavailable"/> when the file holds no signing key
    /// at all (the process takes it from another source); or, as
    /// <see cref="SecretErrors.KeyringFileInconsistent"/>, the reason the file's ring cannot be trusted.
    /// </returns>
    public static ErrorOr<RsaKeyring> Read(SecretConfiguration secrets, string? migratedKid)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        if (string.IsNullOrWhiteSpace(secrets.JwtPrivateKeyPem))
        {
            return SecretErrors.KeyringUnavailable;
        }

        return FromEntries(
            secrets.SigningKeys,
            secrets.SigningCurrentKid,
            secrets.SigningNextKid,
            secrets.JwtPrivateKeyPem,
            ReadRemoved(secrets),
            migratedKid);
    }

    /// <summary>Builds the ring from stored entries, refusing anything no change could have written (KR8).</summary>
    /// <param name="entries">The keys by kid; empty when the ring was never saved.</param>
    /// <param name="currentKid">The current key's kid.</param>
    /// <param name="nextKid">The staged key's kid, if any.</param>
    /// <param name="singlePrivateKeyPem">The single signing key, which mirrors the current key.</param>
    /// <param name="removed">What the ring remembers of keys that left it.</param>
    /// <param name="migratedKid">The kid of the single key while no ring is saved.</param>
    public static ErrorOr<RsaKeyring> FromEntries(
        IReadOnlyDictionary<string, SigningKeyEntry>? entries,
        string? currentKid,
        string? nextKid,
        string singlePrivateKeyPem,
        IReadOnlyList<RemovedSigningKey> removed,
        string? migratedKid)
    {
        var single = PrivateKey(singlePrivateKeyPem, "the single signing key (Jwt:PrivateKeyPem)");
        if (single.IsError)
        {
            return single.Errors;
        }

        if (entries is not { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(currentKid) || !string.IsNullOrWhiteSpace(nextKid))
            {
                return Inconsistent(
                    "The signing keyring names a current or a staged key but holds no key. Restore the most recent copy " +
                    "of the secrets file taken after the last keyring change.");
            }

            // The deployment never changed the ring: its one key is the current key, under the
            // name it has always been published with (KR2), until the first change saves it.
            var kid = string.IsNullOrWhiteSpace(migratedKid) ? RsaKeyring.MigratedKeyId : migratedKid.Trim();
            var (thumbprint, publicKeyPem) = single.Value;
            return RsaKeyring.Create(
                [new SigningKey(kid, thumbprint, publicKeyPem, singlePrivateKeyPem, null, null, null, false)],
                kid,
                nextKid: null,
                removed,
                persisted: false);
        }

        var keys = new List<SigningKey>();
        foreach (var (kid, entry) in entries)
        {
            if (entry is null)
            {
                return Inconsistent($"Signing key {kid} in the secrets file is empty.");
            }

            var signs = string.Equals(kid, currentKid, StringComparison.Ordinal)
                || string.Equals(kid, nextKid, StringComparison.Ordinal);
            var key = signs ? SigningEntry(kid, entry) : VerifyingEntry(kid, entry);
            if (key.IsError)
            {
                return key.Errors;
            }

            keys.Add(key.Value);
        }

        var ring = RsaKeyring.Create(keys, currentKid, nextKid, removed, persisted: true);
        if (ring.IsError)
        {
            return ring.Errors;
        }

        // Every save mirrors the current key into the single key, so the two disagree only after
        // a hand edit, a restore that mixed two copies, or a save by a build without the ring.
        if (!string.Equals(ring.Value.Current.Thumbprint, single.Value.Thumbprint, StringComparison.Ordinal))
        {
            return Inconsistent(
                $"The single signing key (Jwt:PrivateKeyPem) is not the keyring's current key {ring.Value.Current.Kid}. " +
                "Restore the most recent copy of the secrets file taken after the last keyring change.");
        }

        return ring.Value;
    }

    /// <summary>
    /// Writes <paramref name="ring"/> into <paramref name="secrets"/>: every key, a private key for
    /// the current and the staged key only (KR1), the single key mirroring the current one, and what
    /// the ring remembers of keys that left it. Nothing else in the file changes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The ring carries no private key for its current key (a process ring).</exception>
    public static void Write(RsaKeyring ring, SecretConfiguration secrets)
    {
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(secrets);

        if (ring.Current.PrivateKeyPem is null || ring.Current.PublicKeyPem is null)
        {
            throw new InvalidOperationException("Only a ring read from the secrets file can be written back to it.");
        }

        secrets.SigningKeys = ring.Keys.ToDictionary(
            key => key.Kid,
            key => new SigningKeyEntry
            {
                PrivateKeyPem = ring.IsCurrent(key) || ring.IsNext(key) ? key.PrivateKeyPem : null,
                PublicKeyPem = key.PublicKeyPem,
                CreatedAtUtc = key.CreatedAtUtc,
                PublishedAtUtc = key.PublishedAtUtc,
                VerifyUntilUtc = key.VerifyUntilUtc,
                Compromised = key.Compromised
            });
        secrets.SigningCurrentKid = ring.Current.Kid;
        secrets.SigningNextKid = ring.Next?.Kid;
        secrets.JwtPrivateKeyPem = ring.Current.PrivateKeyPem;
        secrets.JwtPublicKeyPem = ring.Current.PublicKeyPem;
        secrets.SigningRemovedKeys = ring.Removed
            .Select(key => new RemovedSigningKeyEntry
            {
                Kid = key.Thumbprint,
                RemovedAtUtc = key.RemovedAtUtc,
                Reason = key.Reason.ToString(),
                Compromised = key.Compromised
            })
            .ToList();
    }

    /// <summary>Whether two keys are the same key under the same name.</summary>
    public static bool SameKey(SigningKey? a, SigningKey? b) =>
        a is null || b is null
            ? a is null && b is null
            : string.Equals(a.Kid, b.Kid, StringComparison.OrdinalIgnoreCase)
              && string.Equals(a.Thumbprint, b.Thumbprint, StringComparison.Ordinal);

    /// <summary>A key read from supplied private-key PEM: validated (a private part, at least 2048 bits) and named by its thumbprint.</summary>
    /// <returns>The key, or the validation error of the material.</returns>
    public static ErrorOr<SigningKey> FromPrivateKeyPem(string privateKeyPem)
    {
        var publicKeyPem = SecretKeyMaterial.ValidateRsaPrivateKey(privateKeyPem);
        if (publicKeyPem.IsError)
        {
            return publicKeyPem.Errors;
        }

        var thumbprint = SigningKeyThumbprint.OfPem(publicKeyPem.Value);
        return new SigningKey(thumbprint, thumbprint, publicKeyPem.Value, privateKeyPem, null, null, null, false);
    }

    // The current and the staged key sign, so they need a usable private key, and the public key
    // stored beside it (when there is one) must be that key's.
    private static ErrorOr<SigningKey> SigningEntry(string kid, SigningKeyEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.PrivateKeyPem))
        {
            return Inconsistent($"Signing key {kid} is the current or the staged key but has no private key.");
        }

        var key = PrivateKey(entry.PrivateKeyPem, $"signing key {kid}");
        if (key.IsError)
        {
            return key.Errors;
        }

        var (thumbprint, publicKeyPem) = key.Value;
        if (!string.IsNullOrWhiteSpace(entry.PublicKeyPem) && PublicThumbprint(entry.PublicKeyPem) != thumbprint)
        {
            return Inconsistent($"The public key stored for signing key {kid} is not its private key's.");
        }

        return Entry(kid, thumbprint, publicKeyPem, entry.PrivateKeyPem, entry);
    }

    // A previous key or a compromised record is only ever verified against (or not at all): its
    // public key is enough, and is all a file saved by this build holds.
    private static ErrorOr<SigningKey> VerifyingEntry(string kid, SigningKeyEntry entry)
    {
        var publicKeyPem = !string.IsNullOrWhiteSpace(entry.PublicKeyPem) ? entry.PublicKeyPem
            : !string.IsNullOrWhiteSpace(entry.PrivateKeyPem) && SecretKeyMaterial.ValidateRsaPrivateKey(entry.PrivateKeyPem) is { IsError: false } derived
                ? derived.Value
                : null;

        if (publicKeyPem is null || PublicThumbprint(publicKeyPem) is not { } thumbprint)
        {
            return Inconsistent($"The public key of signing key {kid} does not parse.");
        }

        return Entry(kid, thumbprint, publicKeyPem, privateKeyPem: null, entry);
    }

    private static SigningKey Entry(string kid, string thumbprint, string publicKeyPem, string? privateKeyPem, SigningKeyEntry entry) =>
        new(
            kid,
            thumbprint,
            publicKeyPem,
            privateKeyPem,
            Utc(entry.CreatedAtUtc),
            Utc(entry.PublishedAtUtc),
            Utc(entry.VerifyUntilUtc),
            entry.Compromised);

    private static ErrorOr<(string Thumbprint, string PublicKeyPem)> PrivateKey(string pem, string what)
    {
        var publicKeyPem = SecretKeyMaterial.ValidateRsaPrivateKey(pem);
        if (publicKeyPem.IsError)
        {
            // The description names the rule broken, never the value.
            return Inconsistent($"{what} is invalid: {publicKeyPem.FirstError.Description}");
        }

        return (SigningKeyThumbprint.OfPem(publicKeyPem.Value), publicKeyPem.Value);
    }

    private static string? PublicThumbprint(string publicKeyPem)
    {
        try
        {
            return SigningKeyThumbprint.OfPem(publicKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    // A file no build with the memory has saved has none. An entry that names no reason the build
    // knows reads as a retire: either way the key is refused.
    private static List<RemovedSigningKey> ReadRemoved(SecretConfiguration secrets) =>
        (secrets.SigningRemovedKeys ?? [])
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Kid))
            .Select(entry => new RemovedSigningKey(
                entry.Kid,
                Utc(entry.RemovedAtUtc),
                Enum.TryParse<SigningKeyRemoval>(entry.Reason, out var reason) && Enum.IsDefined(reason) ? reason : SigningKeyRemoval.Retired,
                entry.Compromised))
            .ToList();

    private static Error Inconsistent(string detail) => SecretErrors.KeyringFileInconsistent(detail);

    // A value written with an offset deserializes as local time: convert it, never relabel it,
    // or the instant moves by the server's offset (as the provider does).
    private static DateTime? Utc(DateTime? value) =>
        value is not { } date ? null
        : date.Kind == DateTimeKind.Local ? date.ToUniversalTime()
        : DateTime.SpecifyKind(date, DateTimeKind.Utc);
}
