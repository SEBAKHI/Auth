using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for user external login records.
/// </summary>
public interface IUserExternalLoginRepository
{
    /// <summary>
    /// Gets an external login by provider and provider user ID.
    /// </summary>
    Task<UserExternalLogin?> GetByProviderAsync(string provider, string providerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all external logins for a user.
    /// </summary>
    Task<IReadOnlyList<UserExternalLogin>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new external login record.
    /// </summary>
    Task CreateAsync(UserExternalLogin login, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing external login record.
    /// </summary>
    Task UpdateAsync(UserExternalLogin login, CancellationToken cancellationToken = default);
}
