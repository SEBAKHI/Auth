using System.Security.Cryptography;
using System.Text;

namespace Auth.Application.Security.Keyring;

/// <summary>How a key left the HMAC keyring.</summary>
public enum HmacKeyRemoval
{
    /// <summary>Removed for good after it stopped issuing.</summary>
    Retired,

    /// <summary>Removed while staged, before it ever issued.</summary>
    Discarded
}

/// <summary>
/// What the HMAC keyring remembers of a key once its material has left the secrets file
/// (OI-217): enough to recognize the material if it is offered again, and to never give
/// its id to another key.
/// </summary>
/// <remarks>
/// A key is retired because it is old or because it leaked, and nothing about the material
/// says which: taken back, it would issue again under a new id, and nothing would mark it
/// leaked any more. So every path that accepts new material refuses one the ring remembers.
/// The ring keeps a fingerprint, never the material, and forgets nothing.
/// </remarks>
/// <param name="Id">The id the key had.</param>
/// <param name="Fingerprint">The fingerprint of its material (<see cref="FingerprintOf"/>).</param>
/// <param name="RemovedAtUtc">When it left the ring; null when the file does not say.</param>
/// <param name="Reason">How it left the ring.</param>
/// <param name="Compromised">Whether it had been marked leaked.</param>
public sealed record RemovedHmacKey(int Id, string Fingerprint, DateTime? RemovedAtUtc, HmacKeyRemoval Reason, bool Compromised)
{
    // Separates this fingerprint from every other use of SHA-256 over the same material.
    private static readonly byte[] FingerprintPrefix = Encoding.ASCII.GetBytes("AuthSystem.HmacKeyring.RemovedKey.v1\0");

    /// <summary>What the ring remembers of <paramref name="key"/> as it leaves.</summary>
    public static RemovedHmacKey Of(HmacKey key, DateTime nowUtc, HmacKeyRemoval reason) =>
        new(key.Id, FingerprintOf(key.Material), nowUtc, reason, key.Compromised);

    /// <summary>
    /// SHA-256 over a fixed prefix and <paramref name="material"/>, base64. One way: what the
    /// file keeps of a removed key can neither issue nor verify anything.
    /// </summary>
    public static string FingerprintOf(ReadOnlySpan<byte> material)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(FingerprintPrefix);
        hash.AppendData(material);
        return Convert.ToBase64String(hash.GetHashAndReset());
    }
}
