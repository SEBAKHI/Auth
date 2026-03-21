namespace Auth_API.Modules.WebhookKeyManagement.Contracts;

/// <summary>
/// Request to create a new webhook key.
/// </summary>
public record CreateWebhookKeyRequest(
    Guid ApplicationId,
    string Name,
    string TargetUrl,
    string? Description = null,
    string? Environment = "production",
    DateTime? ExpiresAt = null);
