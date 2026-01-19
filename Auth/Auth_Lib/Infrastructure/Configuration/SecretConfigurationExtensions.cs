using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth_Lib.Infrastructure.Configuration;

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
    /// </remarks>
    public static IConfigurationBuilder AddDpapiSecrets(
        this IConfigurationBuilder builder,
        IDataProtectionProvider dataProtectionProvider,
        string? secretFilePath = null)
    {
        secretFilePath ??= GetDefaultSecretFilePath();

        builder.Add(new DpapiSecretConfigurationSource(secretFilePath, dataProtectionProvider));
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
