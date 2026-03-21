namespace Auth_API.Modules.OrganizationManagement.Contracts;

public record GrantPermissionRequest(
    Guid ApplicationId,
    Guid PermissionId,
    DateTime? ExpiresAt = null);
