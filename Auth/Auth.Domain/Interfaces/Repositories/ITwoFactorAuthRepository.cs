using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for two-factor authentication operations.
/// </summary>
public interface ITwoFactorAuthRepository
{
    /// <summary>
    /// Gets the 2FA configuration for a user.
    /// </summary>
    Task<TwoFactorAuth?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new 2FA configuration.
    /// </summary>
    Task CreateAsync(TwoFactorAuth twoFactorAuth, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing 2FA configuration.
    /// </summary>
    Task UpdateAsync(TwoFactorAuth twoFactorAuth, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a 2FA configuration.
    /// </summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
