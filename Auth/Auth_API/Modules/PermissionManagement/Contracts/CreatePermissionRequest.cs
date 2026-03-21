namespace Auth_API.Modules.PermissionManagement.Contracts;

public record CreatePermissionRequest(
    Guid ApplicationId,
    string Code,
    string Name,
    string? Description = null,
    Guid? ParentId = null);
