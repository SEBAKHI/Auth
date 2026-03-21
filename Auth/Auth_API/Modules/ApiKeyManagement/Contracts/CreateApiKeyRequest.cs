namespace Auth_API.Modules.ApiKeyManagement.Contracts;

public record CreateApiKeyRequest(
    Guid ApplicationId,
    string Name,
    string? Description = null,
    string? Environment = null,
    int? RateLimitPerMinute = null,
    int? RateLimitPerDay = null,
    DateTime? ExpiresAt = null,
    IReadOnlyList<Guid>? PermissionIds = null);
