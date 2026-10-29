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
    /// Lists API keys, optionally narrowed to one application; a null
    /// <paramref name="applicationId"/> spans every application.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.ApiKeys"/>; null keeps the default order.
    /// </summary>
    /// <remarks>
    /// Named ListAsync, not GetByApplicationAsync: a method whose name promises one
    /// application while returning every application's keys is the exact class of
    /// mismatch this endpoint's dashboard alert already suffered from.
    /// </remarks>
    Task<IReadOnlyList<ApiKey>> ListAsync(
        Guid? applicationId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new API key and its scopes in one transaction: a failing scope leaves no key.
    /// </summary>
    Task<ApiKey> CreateAsync(ApiKey apiKey, IReadOnlyCollection<ApiKeyScope> scopes, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a rotation in one transaction (OI-128 PR B): <paramref name="replacement"/> and its
    /// <paramref name="scopes"/>, and the new expiry of <paramref name="replaced"/>. False, and
    /// nothing written, when <paramref name="replaced"/> was revoked since it was read.
    /// </summary>
    Task<bool> TryRotateAsync(ApiKey replaced, ApiKey replacement, IReadOnlyCollection<ApiKeyScope> scopes, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an API key.
    /// </summary>
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an API key.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a scope from an API key.
    /// </summary>
    Task RemoveScopeAsync(Guid apiKeyId, Guid permissionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the permission codes for an API key.
    /// </summary>
    Task<IReadOnlyList<string>> GetScopesAsync(Guid apiKeyId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the ids of every permission an API key was granted, active or not: what a rotation
    /// copies, after checking each again.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetScopePermissionIdsAsync(Guid apiKeyId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the permission codes for many API keys in one round trip. Keys with no
    /// scopes are absent from the map.
    /// </summary>
    /// <remarks>
    /// The per-key overload made a listing issue one query per row, which was tolerable
    /// only while every listing was scoped to a single application.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetScopesAsync(
        IReadOnlyCollection<Guid> apiKeyIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the last usage of an API key.
    /// </summary>
    Task RecordUsageAsync(Guid apiKeyId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the active (not revoked, not expired) API key that carries <paramref name="selector"/>,
    /// when its application is alive (active, not deleted); otherwise null.
    /// </summary>
    Task<ApiKey?> GetActiveBySelectorAsync(string selector, CancellationToken cancellationToken);
}
