using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Outcome of a concurrency-checked upsert of a settings-override row.
/// </summary>
/// <param name="Success">False when the optimistic-concurrency check failed.</param>
/// <param name="RowVersion">The row's new rowversion after a successful write.</param>
/// <param name="Version">The row's new save counter after a successful write.</param>
public record SystemSettingsUpsertResult(bool Success, byte[]? RowVersion, int? Version);

/// <summary>
/// Repository for per-section system-settings override rows.
/// </summary>
public interface ISystemSettingsRepository
{
    Task<IReadOnlyList<SystemSettingsOverride>> GetAllAsync(CancellationToken cancellationToken);

    Task<SystemSettingsOverride?> GetAsync(string sectionKey, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates the section's override row. When
    /// <paramref name="expectedRowVersion"/> is null the row must not exist
    /// yet; otherwise the stored rowversion must match. A mismatch returns
    /// <c>Success = false</c> instead of writing.
    /// </summary>
    Task<SystemSettingsUpsertResult> UpsertAsync(
        SystemSettingsOverride settings,
        byte[]? expectedRowVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the section's override row (full reset to file values).
    /// Returns false when no row existed.
    /// </summary>
    Task<bool> DeleteAsync(string sectionKey, CancellationToken cancellationToken);
}
