namespace Auth_Lib.Infrastructure.Configuration;

/// <summary>
/// Configuration settings for gateway token validation.
/// </summary>
public class GatewaySettings
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// Gets or sets the header name for the gateway token.
    /// </summary>
    public string TokenHeaderName { get; set; } = "X-Gateway-Token";

    /// <summary>
    /// Gets or sets the expected gateway token value.
    /// This should be a strong random value shared between the gateway and API.
    /// </summary>
    public string ExpectedToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether gateway token validation is enabled.
    /// </summary>
    public bool ValidationEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets paths that are exempt from gateway token validation.
    /// </summary>
    public string[] ExemptPaths { get; set; } = new[]
    {
        "/.well-known/",
        "/health",
        "/ready"
    };
}
