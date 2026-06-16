namespace Auth.Shared.Configuration;

/// <summary>
/// Settings that describe the X.509 certificate used to protect the Data Protection
/// key ring at rest when <see cref="SecretStorageMode.Certificate"/> is selected.
/// Bound from the <c>DataProtection:Certificate</c> configuration section.
/// </summary>
public class DataProtectionCertificateSettings
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "DataProtection:Certificate";

    /// <summary>
    /// Absolute path to a PKCS#12 (<c>.pfx</c>) file that contains the certificate
    /// and its private key. Used when loading the certificate from a file.
    /// </summary>
    public string? PfxPath { get; set; }

    /// <summary>
    /// Password for the <c>.pfx</c> file. Prefer <see cref="PasswordEnvironmentVariable"/>
    /// over setting the password directly in configuration.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Name of an environment variable that holds the <c>.pfx</c> password.
    /// When set, it takes priority over <see cref="Password"/>.
    /// </summary>
    public string? PasswordEnvironmentVariable { get; set; }

    /// <summary>
    /// Thumbprint of a certificate already installed in the Windows certificate store.
    /// Used as an alternative to <see cref="PfxPath"/> when loading from the store.
    /// </summary>
    public string? Thumbprint { get; set; }

    /// <summary>
    /// Optional list of additional <c>.pfx</c> files (previous certificates) that should
    /// still be able to decrypt the key ring after a certificate rotation.
    /// Maps to <c>UnprotectKeysWithAnyCertificate</c>.
    /// </summary>
    public List<string> AdditionalPfxPaths { get; set; } = new();

    /// <summary>
    /// Indicates whether at least one certificate source (file path or store thumbprint) is configured.
    /// </summary>
    public bool HasSource() =>
        !string.IsNullOrWhiteSpace(PfxPath) || !string.IsNullOrWhiteSpace(Thumbprint);

    /// <summary>
    /// Resolves the effective password, preferring the environment variable when configured.
    /// </summary>
    public string? ResolvePassword()
    {
        if (!string.IsNullOrWhiteSpace(PasswordEnvironmentVariable))
        {
            return Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        }

        return Password;
    }
}
