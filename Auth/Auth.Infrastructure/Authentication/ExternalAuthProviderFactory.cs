using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Factory that resolves external authentication providers by name.
/// Providers are registered via DI and collected here.
/// </summary>
public class ExternalAuthProviderFactory : IExternalAuthProviderFactory
{
    private readonly IReadOnlyDictionary<string, IExternalAuthProvider> _providers;

    public ExternalAuthProviderFactory(IEnumerable<IExternalAuthProvider> providers)
    {
        _providers = providers.ToDictionary(
            p => p.ProviderName,
            p => p,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IExternalAuthProvider? GetProvider(string providerName)
    {
        return _providers.TryGetValue(providerName, out var provider) ? provider : null;
    }
}
