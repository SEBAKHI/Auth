using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for external authentication provider registry.
/// </summary>
public interface IExternalAuthProviderRepository
{
    /// <summary>
    /// Gets all enabled external authentication providers, ordered by display order.
    /// </summary>
    Task<IReadOnlyList<ExternalAuthProvider>> GetAllEnabledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets an external authentication provider by its code.
    /// </summary>
    Task<ExternalAuthProvider?> GetByCodeAsync(string code, CancellationToken cancellationToken);
}
