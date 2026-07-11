using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the single-row platform settings aggregate.
/// </summary>
public interface IPlatformSettingsRepository
{
    /// <summary>
    /// Gets the platform settings row, or null when it has not been seeded.
    /// </summary>
    Task<PlatformSettings?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the settings row, inserting it when the seed row is missing.
    /// </summary>
    Task UpdateAsync(PlatformSettings settings, CancellationToken cancellationToken);
}
