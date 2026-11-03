namespace Auth.Application.Security.Keyring;

/// <summary>
/// One key of the RSA signing keyring (S12, KR1): its name, its public key, its private key
/// while it is the current or the staged key, and the dates that place it.
/// </summary>
/// <remarks>
/// A ring read from the secrets file carries the PEMs; the ring a process holds carries none
/// (<see cref="RsaKeyring.WithoutMaterial"/>), because the process keeps its loaded keys
/// elsewhere and only names, states and dates are read from it.
/// </remarks>
/// <param name="Kid">The key's name in every token header and in the JWKS.</param>
/// <param name="Thumbprint">The key's RFC 7638 thumbprint (<see cref="SigningKeyThumbprint"/>).</param>
/// <param name="PublicKeyPem">The public key (SubjectPublicKeyInfo PEM); null in a process ring.</param>
/// <param name="PrivateKeyPem">The private key, for the current and the staged key only; null otherwise and in a process ring.</param>
/// <param name="CreatedAtUtc">When the key was staged; null for the key migrated from the single signing key, whose age is unknown.</param>
/// <param name="PublishedAtUtc">When a process first loaded the staged key and listed it in the JWKS.</param>
/// <param name="VerifyUntilUtc">For a previous key, the last instant a token signed by it is accepted.</param>
/// <param name="Compromised">The key leaked: it is neither accepted nor published.</param>
public sealed record SigningKey(
    string Kid,
    string Thumbprint,
    string? PublicKeyPem,
    string? PrivateKeyPem,
    DateTime? CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? VerifyUntilUtc,
    bool Compromised)
{
    /// <summary>
    /// The key migrated from the single signing key (KR2): it keeps the name it has always
    /// been published under (<c>Jwt:KeyId</c>), which is not its thumbprint.
    /// </summary>
    public bool IsMigrated => !string.Equals(Kid, Thumbprint, StringComparison.Ordinal);
}

/// <summary>How a key left the RSA signing keyring.</summary>
public enum SigningKeyRemoval
{
    /// <summary>Removed for good after it stopped signing, or after it was marked compromised.</summary>
    Retired,

    /// <summary>Removed while staged, before it ever signed.</summary>
    Discarded
}

/// <summary>
/// What the RSA signing keyring remembers of a key once it has left the secrets file (S12):
/// its thumbprint, so the key is refused if it is ever offered again.
/// </summary>
/// <remarks>
/// A key leaves because it is old or because it leaked, and nothing about the key says
/// which: taken back, it would sign again, and nothing would mark it leaked any more. A
/// thumbprint is public (every kid but the migrated key's is one), so the ring keeps it as
/// it is, and forgets nothing.
/// </remarks>
/// <param name="Thumbprint">The key's RFC 7638 thumbprint.</param>
/// <param name="RemovedAtUtc">When it left the ring; null when the file does not say.</param>
/// <param name="Reason">How it left the ring.</param>
/// <param name="Compromised">Whether it had been marked leaked.</param>
public sealed record RemovedSigningKey(string Thumbprint, DateTime? RemovedAtUtc, SigningKeyRemoval Reason, bool Compromised)
{
    /// <summary>What the ring remembers of <paramref name="key"/> as it leaves.</summary>
    public static RemovedSigningKey Of(SigningKey key, DateTime nowUtc, SigningKeyRemoval reason) =>
        new(key.Thumbprint, nowUtc, reason, key.Compromised);
}
