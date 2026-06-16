namespace Auth.Shared.Configuration;

/// <summary>
/// Determines where the application's cryptographic secrets (RSA signing key,
/// HMAC key, gateway token) are stored and how they are protected at rest.
/// </summary>
public enum SecretStorageMode
{
    /// <summary>
    /// Secrets are stored in plain text inside an appsettings JSON file
    /// (for example <c>appsettings.Production.json</c>). No encryption is applied.
    /// Simplest to operate; only as secure as the file-system permissions on the file.
    /// This is the default storage mode.
    /// </summary>
    PlainText = 0,

    /// <summary>
    /// Secrets are stored in an encrypted secrets file. The Data Protection key ring
    /// that protects that file is itself encrypted at rest with an X.509 certificate.
    /// Portable across machines and operating systems as long as the certificate is available.
    /// </summary>
    Certificate = 1,

    /// <summary>
    /// Secrets are stored in an encrypted secrets file. The Data Protection key ring
    /// that protects that file is encrypted at rest with Windows DPAPI.
    /// Windows-only and bound to the machine/account that created it.
    /// </summary>
    Dpapi = 2
}
