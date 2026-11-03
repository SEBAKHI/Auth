using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Authentication;
using Auth.Shared.Configuration;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Signing keys for tests that build a real <c>JwtTokenService</c>. Until X03 a service built
/// with no key in its settings minted an unsaved random one; it now refuses, as it must on a
/// server, so each such test passes a key of its own. Since S12 the service loads no key: the
/// "key material validation" step does, so a test loads it here, the same way.
/// </summary>
internal static class TestSigningKeys
{
    /// <summary>
    /// A new RSA-2048 private key PEM, minted by the production generator. A different key on
    /// every call, as each service had before, so tests that tell two servers apart still can.
    /// </summary>
    public static string NewPrivateKeyPem() => KeyMaterialGenerator.GenerateRsaKeyPair().PrivateKeyPem;

    /// <summary>
    /// The key in <paramref name="settings"/>, loaded and checked by the loader the server uses,
    /// as the one key of a deployment that never rotated (named by <c>Jwt:KeyId</c>).
    /// </summary>
    public static SigningKeyRing Material(JwtSettings settings) =>
        SigningKeyRing.Single(
            SigningKeyLoader.Load(settings, dataProtectionProvider: null),
            settings.KeyId,
            new SigningKeySource(SigningKeyLoader.PemKey, PlainText: true));

    /// <summary>A token service over the key in <paramref name="settings"/>.</summary>
    public static JwtTokenService Service(JwtSettings settings) => new(settings, Material(settings));
}
