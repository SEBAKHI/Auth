namespace Auth_API.Modules.WebhookKeyManagement.Contracts;

/// <summary>
/// Request to rotate a webhook key.
/// </summary>
public record RotateWebhookKeyRequest(int? GracePeriodMinutes = 60);
