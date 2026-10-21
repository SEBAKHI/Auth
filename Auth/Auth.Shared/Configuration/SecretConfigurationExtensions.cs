using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth.Shared.Configuration;

/// <summary>
/// Extension methods for adding DPAPI secret configuration to the configuration builder.
/// </summary>
public static class SecretConfigurationExtensions
{
    /// <summary>
    /// Adds DPAPI secret configuration source to the configuration builder.
    /// Secrets from the DPAPI file will override values from appsettings.json.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="dataProtectionProvider">The data protection provider for decrypting secrets.</param>
    /// <param name="secretFilePath">Optional path to the secret file. If null, uses default location.</param>
    /// <returns>The configuration builder for chaining.</returns>
    /// <remarks>
    /// The default secret file location is:
    /// %LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi
    ///
    /// Secrets are mapped to configuration keys as follows:
    /// - JwtPrivateKeyPem -> Jwt:PrivateKeyPem
    /// - RefreshTokenHmacKey -> Jwt:RefreshTokenHmacKeyPlain
    /// - SmtpPassword -> Email:Password
    /// - GatewayToken -> Gateway:ExpectedToken (Auth API) AND Gateway:Token (API Gateway)
    /// - ConnectionStrings.AuthDb -> ConnectionStrings:AuthDb
    /// - HmacKeys and the HmacCurrentKeyId family -> Keyring:Hmac:*
    /// </remarks>
    /// <param name="onlyKeys">
    /// When set, the only configuration keys taken from the file (for example
    /// <see cref="DpapiSecretFilters.GatewayTokenOnly"/>); null takes every mapped secret.
    /// </param>
    public static IConfigurationBuilder AddDpapiSecrets(
        this IConfigurationBuilder builder,
        IDataProtectionProvider dataProtectionProvider,
        string? secretFilePath = null,
        IReadOnlySet<string>? onlyKeys = null)
    {
        secretFilePath ??= GetDefaultSecretFilePath();

        builder.Add(new DpapiSecretConfigurationSource(secretFilePath, dataProtectionProvider, onlyKeys));
        return builder;
    }

    /// <summary>
    /// Gets the default path for the DPAPI secret file.
    /// </summary>
    /// <returns>The default secret file path.</returns>
    public static string GetDefaultSecretFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AuthSystem",
            "Secrets",
            "secrets.dpapi");
    }
}
