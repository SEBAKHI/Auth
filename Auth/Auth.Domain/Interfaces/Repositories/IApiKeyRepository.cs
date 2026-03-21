using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for API key operations.
/// </summary>
public interface IApiKeyRepository
{
    /// <summary>
    /// Gets an API key by its ID.
    /// </summary>
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets an API key by its hash.
    /// </summary>
    Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all API keys for an application.
    /// </summary>
    Task<IReadOnlyList<ApiKey>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new API key.
    /// </summary>
    Task<ApiKey> CreateAsync(ApiKey apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an API key.
    /// </summary>
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an API key.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a scope to an API key.
    /// </summary>
    Task AddScopeAsync(ApiKeyScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a scope from an API key.
    /// </summary>
    Task RemoveScopeAsync(Guid apiKeyId, Guid permissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the permission codes for an API key.
    /// </summary>
    Task<IReadOnlyList<string>> GetScopesAsync(Guid apiKeyId, CancellationToken cancellationToken);

    /// <summary>
    /// Records the last usage of an API key.
    /// </summary>
    Task RecordUsageAsync(Guid apiKeyId, CancellationToken cancellationToken);
}
