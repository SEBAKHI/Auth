namespace Auth.Sdk.Models;

/// <summary>
/// Result of an API key validation request.
/// </summary>
public class ApiKeyValidationResult
{
    public bool Active { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; set; } = [];
    public int RateLimitPerMinute { get; set; }
    public int RateLimitPerDay { get; set; }
}
