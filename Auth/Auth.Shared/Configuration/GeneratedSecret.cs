namespace Auth.Shared.Configuration;

/// <summary>
/// A secret the startup path can generate when it finds it missing. Each one is
/// named so the startup guard can tell what data in the database was written
/// under the key a new one would replace (<c>KeyRegenerationGuard</c>).
/// </summary>
public enum GeneratedSecret
{
    /// <summary>The RSA key that signs every access token (<c>Jwt:PrivateKeyPem</c>).</summary>
    SigningKey,

    /// <summary>The HMAC key every stored credential fingerprint is computed under (<c>Jwt:RefreshTokenHmacKeyPlain</c>).</summary>
    CredentialHmacKey,

    /// <summary>The token the API Gateway authenticates with (<c>Gateway:ExpectedToken</c>).</summary>
    GatewayToken,

    /// <summary>The permanent account-deletion identifier key (<c>AccountDeletion:IdentifierHmacKeyPlain</c>).</summary>
    IdentifierKey,

    /// <summary>The Argon2id password pepper (<c>Password:Pepper:Keys</c>).</summary>
    PasswordPepper
}
