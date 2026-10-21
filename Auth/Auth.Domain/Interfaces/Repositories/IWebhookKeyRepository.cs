using Auth.Domain.Entities;
using Auth.Domain.ValueObjects;

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
    /// Gets a webhook key by any fingerprint the presented key may be stored under.
    /// </summary>
    Task<WebhookKey?> GetByHashAsync(CredentialHashCandidates candidates, CancellationToken cancellationToken);

    /// <summary>
    /// Lists webhook keys, optionally narrowed to one application; a null
    /// <paramref name="applicationId"/> spans every application.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.WebhookKeys"/>; null keeps the default order.
    /// </summary>
    /// <remarks>Mirrors <see cref="IApiKeyRepository.ListAsync"/> so the two families cannot drift.</remarks>
    Task<IReadOnlyList<WebhookKey>> ListAsync(
        Guid? applicationId,
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
