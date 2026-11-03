using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Application.Security.Keyring;
using Microsoft.IdentityModel.Tokens;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// The signing keys this process loaded, under the ring that places them (S12): an immutable
/// snapshot. The current key signs; <see cref="VerificationKeys"/> and <see cref="PublishedKeys"/>
/// follow the ring's rules (<see cref="RsaKeyring.Published"/>), so the keys a token is accepted
/// under and the keys the JWKS lists are one set.
/// </summary>
/// <remarks>
/// A snapshot never imports a key after it is built: a change of the ring that needs a key this
/// process did not load at start is refused (<see cref="With"/>), and the process keeps its keys
/// until it restarts (D-T7 = A). The keys live as long as the process; nothing disposes them.
/// </remarks>
public sealed class SigningKeyRing : ISigningKeyMaterial
{
    private readonly IReadOnlyDictionary<string, RsaSecurityKey> _loaded;

    private SigningKeyRing(RsaKeyring ring, IReadOnlyDictionary<string, RsaSecurityKey> loaded, SigningKeySource source)
    {
        Ring = ring;
        Source = source;
        _loaded = loaded;
        Current = new SigningCredentials(loaded[ring.Current.Kid], SecurityAlgorithms.RsaSha256);
    }

    /// <summary>The ring the keys are placed by: names, states and dates, no material.</summary>
    public RsaKeyring Ring { get; }

    /// <summary>Where the process took its signing key from.</summary>
    public SigningKeySource Source { get; }

    /// <inheritdoc />
    public SigningCredentials Current { get; }

    /// <summary>
    /// Loads every key of <paramref name="ring"/> from its PEMs: the current and the staged key
    /// with their private keys, every other key with its public key. A compromised record is
    /// loaded too, so the process can name it, but it is never accepted or published.
    /// </summary>
    /// <exception cref="InvalidOperationException">A key does not import (the ring was not read from the secrets file).</exception>
    public static SigningKeyRing Load(RsaKeyring ring, SigningKeySource source)
    {
        var loaded = new Dictionary<string, RsaSecurityKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ring.Keys)
        {
            var pem = ring.IsCurrent(key) || ring.IsNext(key) ? key.PrivateKeyPem : key.PublicKeyPem;
            if (string.IsNullOrWhiteSpace(pem))
            {
                throw new InvalidOperationException($"Signing key {key.Kid} has no key material to load.");
            }

            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            loaded[key.Kid] = new RsaSecurityKey(rsa) { KeyId = key.Kid };
        }

        return new SigningKeyRing(ring.WithoutMaterial(), loaded, source);
    }

    /// <summary>
    /// One key under <paramref name="kid"/>, with no ring to change: a key from
    /// <c>Jwt:PrivateKeyPath</c>, <c>Jwt:PrivateKeyEncrypted</c>, a PlainText settings file, or a test.
    /// </summary>
    /// <remarks>Does not own <paramref name="rsa"/>: whoever created it disposes it.</remarks>
    public static SigningKeyRing Single(RSA rsa, string kid, SigningKeySource source)
    {
        var thumbprint = SigningKeyThumbprint.Of(rsa);
        var ring = RsaKeyring.Create(
            [new SigningKey(kid, thumbprint, null, null, null, null, null, false)],
            kid,
            nextKid: null,
            removed: null,
            persisted: false);

        if (ring.IsError)
        {
            throw new InvalidOperationException($"The signing key cannot be named '{kid}': {ring.FirstError.Description}");
        }

        return new SigningKeyRing(
            ring.Value,
            new Dictionary<string, RsaSecurityKey>(StringComparer.OrdinalIgnoreCase) { [kid] = new RsaSecurityKey(rsa) { KeyId = kid } },
            source);
    }

    /// <summary>
    /// The snapshot after <paramref name="changed"/>, over the keys this process loaded; null when
    /// <paramref name="changed"/> places a key this process never loaded, or another key under a
    /// loaded key's name.
    /// </summary>
    public SigningKeyRing? With(RsaKeyring changed)
    {
        var unloaded = changed.Keys.Any(key =>
            !_loaded.ContainsKey(key.Kid)
            || Ring.Find(key.Kid) is not { } held
            || !string.Equals(held.Thumbprint, key.Thumbprint, StringComparison.Ordinal));
        if (unloaded)
        {
            return null;
        }

        var kept = changed.Keys.ToDictionary(key => key.Kid, key => _loaded[key.Kid], StringComparer.OrdinalIgnoreCase);
        return new SigningKeyRing(changed.WithoutMaterial(), kept, Source);
    }

    /// <inheritdoc />
    public IReadOnlyList<SecurityKey> VerificationKeys(DateTimeOffset now) =>
        Ring.Published(now.UtcDateTime).Select(key => (SecurityKey)_loaded[key.Kid]).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<RsaSecurityKey> PublishedKeys(DateTimeOffset now) =>
        Ring.Published(now.UtcDateTime).Select(key => _loaded[key.Kid]).ToArray();
}
