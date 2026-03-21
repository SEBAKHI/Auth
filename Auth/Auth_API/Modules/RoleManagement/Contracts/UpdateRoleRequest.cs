namespace Auth_API.Modules.RoleManagement.Contracts;

public record UpdateRoleRequest(
    string Name,
    string? Description = null);
