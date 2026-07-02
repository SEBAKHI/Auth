using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for external authentication provider registry.
/// </summary>
public interface IExternalAuthProviderRepository
{
    /// <summary>
    /// Gets all enabled external authentication providers, ordered by display
    /// order by default. <paramref name="sortBy"/> accepts the allow-listed field
    /// names in <see cref="Constants.SortFields.ExternalProviders"/>.
    /// </summary>
    Task<IReadOnlyList<ExternalAuthProvider>> GetAllEnabledAsync(
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets an external authentication provider by its code.
    /// </summary>
    Task<ExternalAuthProvider?> GetByCodeAsync(string code, CancellationToken cancellationToken);
}
