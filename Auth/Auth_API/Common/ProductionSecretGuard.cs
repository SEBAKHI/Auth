using Microsoft.Extensions.Configuration;

namespace Auth_API.Common;

/// <summary>
/// Startup guard that refuses to run in Production when the crown-jewel secrets
/// (the JWT signing private key and the refresh-token HMAC key) are present in
/// plaintext in the configuration files. In Production these must live only in
/// encrypted secret storage (SecretManagement:StorageMode = Certificate/Dpapi),
/// never as plaintext in appsettings — a plaintext copy on disk defeats
/// encryption at rest and lets anyone who reads the file forge any token.
///
/// MUST be called BEFORE the DPAPI secret provider is layered onto configuration:
/// that provider injects the DECRYPTED key as Jwt:PrivateKeyPem, which would
/// otherwise be indistinguishable from a plaintext file value.
/// </summary>
public static class ProductionSecretGuard
{
    private static readonly string[] PlaintextSecretKeys =
    [
        "Jwt:PrivateKeyPem",
        "Jwt:RefreshTokenHmacKeyPlain",
        "AccountDeletion:IdentifierHmacKeyPlain",
        "ExternalAuth:Apple:PrivateKeyPem"
    ];

    /// <summary>
    /// Sections whose every child is key material (S11, S12): a keyring's keys and
    /// their state. A section is read child by child, because <c>configuration[key]</c>
    /// of a section is null even when the section is full.
    /// </summary>
    private static readonly string[] PlaintextSecretSections =
    [
        "Keyring:Hmac",
        "Keyring:Rsa"
    ];

    public static void EnsureNoPlaintextSecrets(IConfiguration configuration, bool isProduction)
    {
        if (!isProduction)
        {
            return;
        }

        var found = PlaintextSecretKeys
            .Where(key => !string.IsNullOrWhiteSpace(configuration[key]))
            .Concat(PlaintextSecretSections.SelectMany(section => configuration
                .GetSection(section)
                .AsEnumerable()
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => pair.Key)))
            .ToList();

        if (found.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start: plaintext secret(s) [{string.Join(", ", found)}] were found in the " +
            "Production configuration. The JWT signing key, the refresh-token HMAC key and the keyrings must never be " +
            "stored in plaintext in appsettings. Use SecretManagement:StorageMode = Certificate (or Dpapi) " +
            "so they live encrypted in the secrets file, and REMOVE these plaintext values (and Jwt:PublicKeyPem) " +
            "from appsettings.Production.json. If they were ever committed or exposed, rotate the keys.");
    }
}
