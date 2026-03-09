namespace Auth_Lib.Application.Interfaces;

/// <summary>
/// Service for checking user permissions.
/// </summary>
public interface IPermissionChecker
{
    /// <summary>
    /// Checks if a user has a specific permission.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="permission">The permission code to check (e.g., "users:read").</param>
    /// <param name="applicationId">Optional application ID for scoped permissions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the user has the permission, false otherwise.</returns>
    Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has any of the specified permissions.
    /// </summary>
    Task<bool> HasAnyPermissionAsync(
        Guid userId,
        IEnumerable<string> permissions,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a user has all of the specified permissions.
    /// </summary>
    Task<bool> HasAllPermissionsAsync(
        Guid userId,
        IEnumerable<string> permissions,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all effective permissions for a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default);
}
