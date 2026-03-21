using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth.Infrastructure.Configuration;

/// <summary>
/// Configuration source that loads secrets from DPAPI-encrypted file.
/// Delegates to Auth.Shared implementation.
/// </summary>
public class DpapiSecretConfigurationSource : IConfigurationSource
{
    private readonly Auth.Shared.Configuration.DpapiSecretConfigurationSource _inner;

    public DpapiSecretConfigurationSource(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider)
    {
        _inner = new Auth.Shared.Configuration.DpapiSecretConfigurationSource(
            secretFilePath, dataProtectionProvider);
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return _inner.Build(builder);
    }
}
