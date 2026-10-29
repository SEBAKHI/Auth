namespace Auth.Sdk.Models;

/// <summary>
/// Result of an API key validation request: one of this application's own keys.
/// </summary>
public class ApiKeyValidationResult
{
    public bool Active { get; set; }
    public Guid ApiKeyId { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The code of the key's application. The SDK accepts a result only when it is this
    /// application's <see cref="AuthSystemOptions.ClientId"/>.
    /// </summary>
    public string ApplicationCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>
    /// The addresses the key may be presented from: IPv4 and IPv6 addresses and CIDR ranges.
    /// Empty means any address. The <c>ApiKey</c> scheme refuses a request from any other address.
    /// </summary>
    public IReadOnlyList<string> AllowedIps { get; set; } = [];
}
