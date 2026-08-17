using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for role operations.
/// </summary>
public interface IRoleRepository
{
    /// <summary>
    /// Gets a role by its ID.
    /// </summary>
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a role by its code within an application.
    /// </summary>
    Task<Role?> GetByCodeAsync(Guid applicationId, string code, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a role by its code, optionally within an application.
    /// Pass null applicationId for organization-level roles.
    /// </summary>
    Task<Role?> GetByCodeAsync(Guid? applicationId, string code, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active roles. <paramref name="sortBy"/> accepts the allow-listed
    /// field names in <see cref="Constants.SortFields.Roles"/>; null keeps the default order.
    /// </summary>
    Task<IReadOnlyList<Role>> GetAllAsync(
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all roles for an application. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Roles"/>.
    /// </summary>
    Task<IReadOnlyList<Role>> GetByApplicationAsync(
        Guid applicationId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets roles assigned to a user.
    /// </summary>
    Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets roles assigned to a user for a specific application.
    /// </summary>
    Task<IReadOnlyList<Role>> GetUserRolesForApplicationAsync(Guid userId, Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a role code exists in an application.
    /// </summary>
    Task<bool> ExistsByCodeAsync(Guid? applicationId, string code, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new role.
    /// </summary>
    Task<Role> CreateAsync(Role role, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing role.
    /// </summary>
    Task UpdateAsync(Role role, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a role.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Assigns a role to a user.
    /// </summary>
    Task AssignToUserAsync(UserRole userRole, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a role from a user.
    /// </summary>
    Task RemoveFromUserAsync(Guid userId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a user has a specific role.
    /// </summary>
    Task<bool> UserHasRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the distinct users holding an active assignment of the role
    /// (direct or through an organization), with pagination.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.RoleUsers"/>.
    /// </summary>
    Task<(IReadOnlyList<ReadModels.Access.RoleUserRow> Users, int TotalCount)> GetUsersPagedAsync(
        Guid roleId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the distinct applications related to the role: its owning
    /// application and applications appearing on active assignments of the role.
    /// </summary>
    Task<IReadOnlyList<ReadModels.Access.RoleApplicationRow>> GetRoleApplicationsAsync(
        Guid roleId,
        CancellationToken cancellationToken);
}
