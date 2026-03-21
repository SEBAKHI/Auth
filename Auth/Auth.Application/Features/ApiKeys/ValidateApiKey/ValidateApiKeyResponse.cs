namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// Response returned when validating an API key.
/// </summary>
public class ValidateApiKeyResponse
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
