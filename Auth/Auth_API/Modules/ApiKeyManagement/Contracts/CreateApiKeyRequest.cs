namespace Auth_API.Modules.ApiKeyManagement.Contracts;

/// <param name="PermissionIds">The key's scopes: at least one, each an active permission of the application (OI-128 PR B).</param>
/// <param name="AllowedIps">Optional: the IPv4 or IPv6 addresses and CIDR ranges the key may be presented from, at most 50. None means any address.</param>
public record CreateApiKeyRequest(
    Guid ApplicationId,
    string Name,
    string? Description = null,
    string? Environment = null,
    DateTime? ExpiresAt = null,
    IReadOnlyList<Guid>? PermissionIds = null,
    IReadOnlyList<string>? AllowedIps = null);
