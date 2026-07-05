using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for application operations.
/// </summary>
public interface IApplicationRepository
{
    /// <summary>
    /// Gets an application by its ID.
    /// </summary>
    Task<AppEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets an application by its code.
    /// </summary>
    Task<AppEntity?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all applications.
    /// </summary>
    Task<IReadOnlyList<AppEntity>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active applications.
    /// </summary>
    Task<IReadOnlyList<AppEntity>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an application code exists.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new application.
    /// </summary>
    Task<AppEntity> CreateAsync(AppEntity application, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing application.
    /// </summary>
    Task UpdateAsync(AppEntity application, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an application.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets applications with pagination and optional filtering.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.Applications"/>; null keeps the default order.
    /// </summary>
    Task<(IReadOnlyList<AppEntity> Applications, int TotalCount)> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? search,
        bool? isActive,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an application has active API keys.
    /// </summary>
    Task<bool> HasActiveApiKeysAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an application has active user role assignments.
    /// </summary>
    Task<bool> HasActiveUserAssignmentsAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if an application is enabled for any organizations.
    /// </summary>
    Task<bool> HasActiveOrganizationsAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets roles for an application. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Roles"/>.
    /// </summary>
    Task<IReadOnlyList<Auth.Domain.Entities.Role>> GetRolesAsync(
        Guid applicationId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets permissions for an application. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Permissions"/>.
    /// </summary>
    Task<IReadOnlyList<Auth.Domain.Entities.Permission>> GetPermissionsAsync(
        Guid applicationId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the distinct users holding an active role assignment scoped to the
    /// application (direct or through an organization), with pagination.
    /// <paramref name="sortBy"/> accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.ApplicationUsers"/>.
    /// </summary>
    Task<(IReadOnlyList<ReadModels.Access.ApplicationUserRow> Users, int TotalCount)> GetUsersPagedAsync(
        Guid applicationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the organizations that have the application enabled (including
    /// inactive enablement links), with pagination. <paramref name="sortBy"/>
    /// accepts the allow-listed field names in
    /// <see cref="Constants.SortFields.ApplicationOrganizations"/>.
    /// </summary>
    Task<(IReadOnlyList<ReadModels.Access.ApplicationOrganizationRow> Organizations, int TotalCount)> GetOrganizationsPagedAsync(
        Guid applicationId,
        int pageNumber,
        int pageSize,
        string? searchTerm,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);
}
