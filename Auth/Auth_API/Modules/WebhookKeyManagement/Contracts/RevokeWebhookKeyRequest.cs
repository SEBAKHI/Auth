namespace Auth_API.Modules.WebhookKeyManagement.Contracts;

/// <summary>
/// Request to revoke a webhook key.
/// </summary>
public record RevokeWebhookKeyRequest(string? Reason = null);
