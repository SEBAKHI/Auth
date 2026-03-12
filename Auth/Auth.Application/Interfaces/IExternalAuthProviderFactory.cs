namespace Auth.Application.Interfaces;

/// <summary>
/// Factory for resolving external authentication providers by name.
/// </summary>
public interface IExternalAuthProviderFactory
{
    /// <summary>
    /// Gets the external authentication provider for the given provider name.
    /// </summary>
    /// <param name="providerName">The provider code (e.g., "google").</param>
    /// <returns>The provider implementation, or null if the provider is not supported.</returns>
    IExternalAuthProvider? GetProvider(string providerName);
}
