namespace Auth_API.Modules.WebhookKeyManagement.Contracts;

/// <summary>
/// Request to validate a webhook key.
/// </summary>
public record ValidateWebhookKeyRequest(string WebhookKey);
