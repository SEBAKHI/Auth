namespace Auth_API.Modules.UserManagement.Contracts;

public record GrantPermissionRequest(
    Guid PermissionId,
    Guid? ApplicationId = null,
    DateTime? ExpiresAt = null);
