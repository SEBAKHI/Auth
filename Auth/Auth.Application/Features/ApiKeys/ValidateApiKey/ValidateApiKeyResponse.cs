namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// Response returned when validating an API key.
/// </summary>
public class ValidateApiKeyResponse
{
    public bool Active { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The code of the key's application, which is always the caller's (OI-128 PR B); the SDK
    /// refuses an answer that names another.
    /// </summary>
    public string ApplicationCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>
    /// The addresses the key may be presented from, normalized: IPv4 and IPv6 addresses and CIDR
    /// ranges. Empty means any address. The caller's server applies it.
    /// </summary>
    public IReadOnlyList<string> AllowedIps { get; set; } = [];
}
