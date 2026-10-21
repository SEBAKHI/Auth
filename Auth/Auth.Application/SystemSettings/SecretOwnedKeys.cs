namespace Auth.Application.SystemSettings;

/// <summary>
/// Configuration key prefixes owned by the secret layer (encrypted secret
/// file / environment). The system-settings feature must never read or write
/// them: the write path rejects them and the database configuration provider
/// filters them, so the two layers hold disjoint key sets by construction.
/// </summary>
public static class SecretOwnedKeys
{
    private static readonly string[] Prefixes =
    [
        "Jwt:PrivateKey",                        // PrivateKeyPath / PrivateKeyPem / PrivateKeyEncrypted
        "Jwt:PublicKeyPem",
        // Exact name, NOT the shared "Jwt:RefreshToken" prefix: that would
        // also swallow the editable Jwt:RefreshTokenLifetimeDays field.
        "Jwt:RefreshTokenHmacKeyPlain",
        // The keyrings (S11, S12), each by its own prefix and never by "Keyring:",
        // which would also swallow any non-secret key placed beside them.
        "Keyring:Hmac:",
        "Keyring:Rsa:",
        "Email:Password",
        "Gateway:ExpectedToken",
        "Gateway:Token",
        "Password:Pepper:Keys",
        "Password:Pepper:CurrentKeyId",
        "ExternalAuth:Apple:PrivateKeyPem",
        "AccountDeletion:IdentifierHmacKeyPlain",
        "ConnectionStrings:",
        "Secrets:",
        "DataProtection:Certificate:Password"
    ];

    /// <summary>
    /// Returns true when the absolute configuration key belongs to the
    /// secret layer.
    /// </summary>
    public static bool IsSecretOwned(string fullConfigKey)
    {
        foreach (var prefix in Prefixes)
        {
            if (fullConfigKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
