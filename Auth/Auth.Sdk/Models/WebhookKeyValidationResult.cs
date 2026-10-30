namespace Auth.Sdk.Models;

/// <summary>
/// Result of a webhook key validation request: one of this application's own keys.
/// </summary>
public class WebhookKeyValidationResult
{
    public bool Active { get; set; }
    public Guid WebhookKeyId { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The code of the key's application. The SDK accepts a result only when it is this
    /// application's <see cref="AuthSystemOptions.ClientId"/>.
    /// </summary>
    public string ApplicationCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}
