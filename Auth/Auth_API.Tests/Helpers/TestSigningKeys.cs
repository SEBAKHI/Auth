using Auth.Shared.Configuration;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// Signing keys for tests that build a real <c>JwtTokenService</c>. Until X03 a service built
/// with no key in its settings minted an unsaved random one; it now refuses, as it must on a
/// server, so each such test passes a key of its own.
/// </summary>
internal static class TestSigningKeys
{
    /// <summary>
    /// A new RSA-2048 private key PEM, minted by the production generator. A different key on
    /// every call, as each service had before, so tests that tell two servers apart still can.
    /// </summary>
    public static string NewPrivateKeyPem() => KeyMaterialGenerator.GenerateRsaKeyPair().PrivateKeyPem;
}
