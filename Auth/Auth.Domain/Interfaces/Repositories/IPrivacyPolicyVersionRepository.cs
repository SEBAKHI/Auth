using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Persistence port for the privacy-policy revision registry.
/// </summary>
public interface IPrivacyPolicyVersionRepository
{
    /// <summary>
    /// Gets every recorded revision, newest version first.
    /// </summary>
    Task<IReadOnlyList<PrivacyPolicyVersion>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets one revision by its "YYYY.MM" version, or null.
    /// </summary>
    Task<PrivacyPolicyVersion?> GetByVersionAsync(string version, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the revision; returns false when the version already exists
    /// (the unique index arbitrates the race).
    /// </summary>
    Task<bool> TryCreateAsync(PrivacyPolicyVersion version, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the notified-at/count stamped by
    /// <see cref="PrivacyPolicyVersion.MarkNotified"/>.
    /// </summary>
    Task UpdateNotifiedAsync(PrivacyPolicyVersion version, CancellationToken cancellationToken);
}
