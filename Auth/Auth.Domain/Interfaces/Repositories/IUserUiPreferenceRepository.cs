using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for per-user client display preferences.
/// </summary>
public interface IUserUiPreferenceRepository
{
    /// <summary>
    /// Gets every preference the user holds. The set is bounded by
    /// <see cref="UserUiPreference.MaxKeysPerUser"/>, so it is always safe to
    /// read whole.
    /// </summary>
    Task<IReadOnlyList<UserUiPreference>> GetAllForUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Counts the user's preferences, for the per-user key limit.</summary>
    Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or replaces one key. Concurrent writes to the same key resolve
    /// to last-write-wins rather than failing.
    /// </summary>
    Task UpsertAsync(UserUiPreference preference, CancellationToken cancellationToken);

    /// <summary>Removes one key, if the user holds it.</summary>
    Task DeleteAsync(Guid userId, string key, CancellationToken cancellationToken);
}
