namespace Auth_API.Modules.UserManagement.Contracts;

public record AssignRoleRequest(
    Guid RoleId,
    DateTime? ExpiresAt = null);
