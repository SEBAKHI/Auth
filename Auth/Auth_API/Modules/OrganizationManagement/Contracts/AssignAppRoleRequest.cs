namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record AssignAppRoleRequest(
    Guid ApplicationId,
    Guid RoleId,
    DateTime? ExpiresAt = null);
