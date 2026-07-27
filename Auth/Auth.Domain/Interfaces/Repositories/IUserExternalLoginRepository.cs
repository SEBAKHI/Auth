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
    Task<UserExternalLogin?> GetByProviderAsync(string provider, string providerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all external logins for a user.
    /// </summary>
    Task<IReadOnlyList<UserExternalLogin>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new external login record.
    /// </summary>
    Task CreateAsync(UserExternalLogin login, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing external login record.
    /// </summary>
    Task UpdateAsync(UserExternalLogin login, CancellationToken cancellationToken);

    /// <summary>
    /// Sets or clears the encrypted provider refresh token on a login row
    /// (targeted column update — never part of the cached-info update).
    /// </summary>
    /// <param name="loginId">The external login row ID.</param>
    /// <param name="encryptedToken">The per-user-encrypted token, or null to clear after revocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateProviderRefreshTokenAsync(Guid loginId, string? encryptedToken, CancellationToken cancellationToken);
}
