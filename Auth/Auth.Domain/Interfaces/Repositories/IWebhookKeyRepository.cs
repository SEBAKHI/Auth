using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for webhook key operations.
/// </summary>
public interface IWebhookKeyRepository
{
    /// <summary>
    /// Gets a webhook key by its ID.
    /// </summary>
    Task<WebhookKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a webhook key by its HMAC-SHA256 hash.
    /// </summary>
    Task<WebhookKey?> GetByHashAsync(string keyHash, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all webhook keys for an application. <paramref name="sortBy"/> accepts
    /// the allow-listed field names in <see cref="Constants.SortFields.WebhookKeys"/>;
    /// null keeps the default order.
    /// </summary>
    Task<IReadOnlyList<WebhookKey>> GetByApplicationAsync(
        Guid applicationId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new webhook key.
    /// </summary>
    Task<WebhookKey> CreateAsync(WebhookKey webhookKey, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a webhook key.
    /// </summary>
    Task UpdateAsync(WebhookKey webhookKey, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a webhook key.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Records the last usage of a webhook key.
    /// </summary>
    Task RecordUsageAsync(Guid webhookKeyId, CancellationToken cancellationToken);
}
