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
    /// Persists the branding (name, logos, favicon) and the modification stamp,
    /// inserting the row when the seed row is missing. The appearance is not
    /// written: see <see cref="UpdateThemeAsync"/>.
    /// </summary>
    Task UpdateAsync(PlatformSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the appearance and the modification stamp only, inserting the
    /// row when the seed row is missing. Writing the two halves separately keeps
    /// a logo upload from restoring the colours it read a moment earlier, and a
    /// colour change from restoring the logos.
    /// </summary>
    Task UpdateThemeAsync(PlatformSettings settings, CancellationToken cancellationToken);
}
