namespace Auth.Application.DTOs;

/// <summary>
/// Data transfer object for webhook key information.
/// </summary>
public class WebhookKeyDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string KeyPrefix { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Response when creating a webhook key (includes the actual key - only shown once).
/// </summary>
public class CreateWebhookKeyResponse
{
    public Guid Id { get; set; }
    public string WebhookKey { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Response when rotating a webhook key.
/// </summary>
public class RotateWebhookKeyResponse
{
    public string NewWebhookKey { get; set; } = string.Empty;
    public Guid NewWebhookKeyId { get; set; }
    public string NewKeyPrefix { get; set; } = string.Empty;
    public DateTime? OldKeyExpiresAt { get; set; }
    public Guid OldWebhookKeyId { get; set; }
    public string Message { get; set; } = string.Empty;
}
