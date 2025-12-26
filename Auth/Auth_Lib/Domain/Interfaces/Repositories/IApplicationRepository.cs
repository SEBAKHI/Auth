using AppEntity = Auth_Lib.Domain.Entities.Application;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for application operations.
/// </summary>
public interface IApplicationRepository
{
    /// <summary>
    /// Gets an application by its ID.
    /// </summary>
    Task<AppEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an application by its code.
    /// </summary>
    Task<AppEntity?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all applications.
    /// </summary>
    Task<IReadOnlyList<AppEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active applications.
    /// </summary>
    Task<IReadOnlyList<AppEntity>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an application code exists.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new application.
    /// </summary>
    Task<AppEntity> CreateAsync(AppEntity application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing application.
    /// </summary>
    Task UpdateAsync(AppEntity application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an application.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
