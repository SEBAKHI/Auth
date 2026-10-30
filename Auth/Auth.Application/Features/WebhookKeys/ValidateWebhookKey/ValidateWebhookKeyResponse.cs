namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// Response returned when validating a webhook key.
/// </summary>
public class ValidateWebhookKeyResponse
{
    public bool Active { get; set; }
    public Guid WebhookKeyId { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The code of the key's application, which is always the caller's (OI-128 PR B); the SDK
    /// refuses an answer that names another.
    /// </summary>
    public string ApplicationCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}
