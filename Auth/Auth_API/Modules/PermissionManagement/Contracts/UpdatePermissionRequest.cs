namespace Auth_API.Modules.PermissionManagement.Contracts;

public record UpdatePermissionRequest(
    string Name,
    string? Description = null);
