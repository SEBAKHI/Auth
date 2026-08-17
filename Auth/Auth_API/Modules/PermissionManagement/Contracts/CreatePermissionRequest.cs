namespace Auth_API.Modules.PermissionManagement.Contracts;

/// <remarks>
/// Omit <see cref="ApplicationId"/> for a permission belonging to the platform
/// itself, which is how every permission the API enforces is scoped.
/// </remarks>
public record CreatePermissionRequest(
    Guid? ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    Guid? ParentId = null);
