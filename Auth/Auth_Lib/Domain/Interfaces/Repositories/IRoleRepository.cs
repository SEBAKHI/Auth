using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for role operations.
/// </summary>
public interface IRoleRepository
{
    /// <summary>
    /// Gets a role by its ID.
    /// </summary>
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a role by its code within an application.
    /// </summary>
    Task<Role?> GetByCodeAsync(Guid applicationId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a role by its code, optionally within an application.
    /// Pass null applicationId for organization-level roles.
    /// </summary>
    Task<Role?> GetByCodeAsync(Guid? applicationId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles for an application.
    /// </summary>
    Task<IReadOnlyList<Role>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles assigned to a user.
    /// </summary>
    Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets roles assigned to a user for a specific application.
    /// </summary>
    Task<IReadOnlyList<Role>> GetUserRolesForApplicationAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a role code exists in an application.
    /// </summary>
    Task<bool> ExistsByCodeAsync(Guid applicationId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new role.
    /// </summary>
    Task<Role> CreateAsync(Role role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing role.
    /// </summary>
    Task UpdateAsync(Role role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a role.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    Task AssignToUserAsync(UserRole userRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a role from a user.
    /// </summary>
    Task RemoveFromUserAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has a specific role.
    /// </summary>
    Task<bool> UserHasRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
}
