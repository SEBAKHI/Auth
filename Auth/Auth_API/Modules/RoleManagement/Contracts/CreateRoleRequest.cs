namespace Auth_API.Modules.RoleManagement.Contracts;

public record CreateRoleRequest(
    Guid? ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    IReadOnlyList<Guid>? PermissionIds = null);
