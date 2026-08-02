namespace Auth.Application.Configuration;

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
    /// The exempt paths used when configuration supplies none at all. Applied by
    /// <see cref="SettingsArrayNormalizer"/> AFTER binding — never as the property
    /// initializer, because the configuration binder APPENDS configured entries to
    /// whatever array the property already holds, which would make these three
    /// permanently unremovable from any layer (see SettingsArrayNormalizer).
    /// </summary>
    public static readonly string[] DefaultExemptPaths =
    [
        "/.well-known/",
        "/health",
        "/ready"
    ];

    /// <summary>
    /// Gets or sets paths that are exempt from gateway token validation.
    /// Starts empty on purpose; <see cref="DefaultExemptPaths"/> is substituted
    /// after binding only when configuration provides nothing.
    /// </summary>
    public string[] ExemptPaths { get; set; } = [];
}
