using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Proves the loaded signing key can do its job: sign a token and validate it (X03). The
/// readiness check asks this rather than for the public key, because a key that only holds a
/// public part still exports one.
/// </summary>
public interface ISigningKeyProbe
{
    /// <summary>
    /// Signs a short-lived probe token with the signing key and validates it with the same
    /// parameters every access token is validated with. Never issued to anyone.
    /// </summary>
    /// <returns>Success, or the validation error of the probe token.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key cannot sign.</exception>
    ErrorOr<Success> ProbeSigningKey();
}
