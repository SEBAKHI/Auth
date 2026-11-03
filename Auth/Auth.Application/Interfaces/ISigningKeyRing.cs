using Auth.Application.Security.Keyring;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// The RSA signing keyring this process signs and verifies under (S12), as the application
/// layer sees it: names, states and dates, never key material, and no token library type.
/// </summary>
/// <remarks>
/// A process loads its keys once, at start. A staged key enters it only at the next start
/// (D-T7 = A), so a change of the secrets file reaches this process only through
/// <see cref="TryApply"/>, and only when every key the result needs was loaded here.
/// </remarks>
public interface ISigningKeyRing
{
    /// <summary>Where this process took its signing key from.</summary>
    SigningKeySource Source { get; }

    /// <summary>The ring this process holds now, without material.</summary>
    RsaKeyring Current { get; }

    /// <summary>
    /// Applies <paramref name="change"/> to this process's ring, once the secrets file holds its
    /// result: the signer and every validator move to the result at once. Nothing changes, and
    /// false is returned, when the change fails on this ring or its result needs a key this
    /// process never loaded; the next start then loads the file as it is.
    /// </summary>
    bool TryApply(Func<RsaKeyring, ErrorOr<RsaKeyring>> change);
}

/// <summary>
/// The setting this process loaded its signing key from, which decides whether it has a
/// keyring at all (S12, fact 5). Only a key kept in the encrypted secrets file has one: a key
/// read from <c>Jwt:PrivateKeyPath</c> or <c>Jwt:PrivateKeyEncrypted</c>, or from a settings
/// file in PlainText mode, is one key that only a restart replaces.
/// </summary>
/// <param name="Setting">The configuration key the loader took the key from (<c>Jwt:PrivateKeyPath</c>, <c>Jwt:PrivateKeyEncrypted</c> or <c>Jwt:PrivateKeyPem</c>).</param>
/// <param name="PlainText">Whether secrets are kept in PlainText mode, where <c>Jwt:PrivateKeyPem</c> comes from a settings file.</param>
/// <param name="MigratedKid">
/// The kid this process publishes the single key under while the file holds no ring
/// (<see cref="RsaKeyring.MigratedKidFrom"/> of <c>Jwt:KeyId</c>, read once at start). A change
/// that first saves the ring saves that key under this name, so an edit of <c>Jwt:KeyId</c> after
/// the start never renames the key this process publishes.
/// </param>
public sealed record SigningKeySource(string Setting, bool PlainText, string MigratedKid = RsaKeyring.MigratedKeyId)
{
    /// <summary>The setting the secrets file supplies the signing key through.</summary>
    public const string SecretsFileSetting = "Jwt:PrivateKeyPem";

    /// <summary>Whether this process keeps its signing key in a keyring it can change.</summary>
    public bool HoldsRing => !PlainText && string.Equals(Setting, SecretsFileSetting, StringComparison.Ordinal);

    /// <summary>Why there is no keyring, for the console: <c>PlainText</c>, <c>ExternalSource</c>, or null when there is one.</summary>
    public string? UnavailableReason => HoldsRing ? null : PlainText ? "PlainText" : "ExternalSource";
}
