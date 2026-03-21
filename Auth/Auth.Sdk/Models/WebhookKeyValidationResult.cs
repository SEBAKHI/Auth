namespace Auth.Sdk.Models;

/// <summary>
/// Result of a webhook key validation request.
/// </summary>
public class WebhookKeyValidationResult
{
    public bool Active { get; set; }
    public Guid WebhookKeyId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}
