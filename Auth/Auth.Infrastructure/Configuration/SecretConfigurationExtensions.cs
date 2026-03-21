using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Configuration;

/// <summary>
/// Extension methods for adding DPAPI secret configuration.
/// Delegates to Auth.Shared implementation.
/// </summary>
public static class SecretConfigurationExtensions
{
    /// <summary>
    /// Adds DPAPI secret configuration source to the configuration builder.
    /// </summary>
    public static IConfigurationBuilder AddDpapiSecrets(
        this IConfigurationBuilder builder,
        IDataProtectionProvider dataProtectionProvider,
        string? secretFilePath = null)
    {
        return Auth.Shared.Configuration.SecretConfigurationExtensions
            .AddDpapiSecrets(builder, dataProtectionProvider, secretFilePath);
    }

    /// <summary>
    /// Gets the default path for the DPAPI secret file.
    /// </summary>
    public static string GetDefaultSecretFilePath()
    {
        return Auth.Shared.Configuration.SecretConfigurationExtensions.GetDefaultSecretFilePath();
    }
}
