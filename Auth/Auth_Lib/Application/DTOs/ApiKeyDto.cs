namespace Auth_Lib.Application.DTOs;

/// <summary>
/// Data transfer object for API key information.
/// </summary>
public class ApiKeyDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public int RateLimitPerMinute { get; set; }
    public int RateLimitPerDay { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public IReadOnlyList<string> Scopes { get; set; } = [];
}

/// <summary>
/// Response when creating an API key (includes the actual key - only shown once).
/// </summary>
public class CreateApiKeyResponse
{
    public Guid Id { get; set; }
    public string ApiKey { get; set; } = string.Empty;  // The actual key - only returned on creation
    public string KeyPrefix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
