namespace Auth.Application.Interfaces;

/// <summary>
/// Generates new key material, from the one generator every storage mode uses
/// (KeyMaterialGenerator, C10 item 3), so a key the keyring stages is made exactly as
/// the first key of a new secrets file is.
/// </summary>
public interface IKeyMaterialGenerator
{
    /// <summary>A new 256-bit HMAC key, base64.</summary>
    string GenerateHmacKeyBase64();

    /// <summary>A new RSA-2048 signing key: its PKCS#8 private key PEM and its SubjectPublicKeyInfo public key PEM (S12).</summary>
    (string PrivateKeyPem, string PublicKeyPem) GenerateRsaKeyPair();
}
