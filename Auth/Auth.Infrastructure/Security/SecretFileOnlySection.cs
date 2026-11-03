using Auth.Shared.Configuration;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Finds values of a configuration section that a source other than the encrypted secrets file
/// supplies (S11 SEC-1, S12). A keyring may come from that file alone: a value from an
/// environment variable or a settings file would sit above the file and decide which key
/// signs or verifies what.
/// </summary>
public static class SecretFileOnlySection
{
    /// <summary>
    /// Every key under <paramref name="sectionName"/> that a provider other than the secrets
    /// file holds a non-empty value for, each with the provider that holds it; null when the
    /// providers cannot be inspected.
    /// </summary>
    public static IReadOnlyList<string>? ForeignValues(IConfiguration configuration, string sectionName)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return null;
        }

        return root.Providers
            .Where(provider => provider is not DpapiSecretConfigurationProvider)
            .SelectMany(provider => ValuedKeys(provider, sectionName).Select(key => $"{key} ({provider})"))
            .ToList();
    }

    // Every key under parentPath that the provider itself holds a non-empty value for.
    private static IEnumerable<string> ValuedKeys(IConfigurationProvider provider, string parentPath)
    {
        foreach (var child in provider.GetChildKeys([], parentPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = ConfigurationPath.Combine(parentPath, child);
            if (provider.TryGet(path, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                yield return path;
            }

            foreach (var nested in ValuedKeys(provider, path))
            {
                yield return nested;
            }
        }
    }
}
