namespace Auth.Sdk;

/// <summary>
/// Constants used by the AuthSystem SDK.
/// </summary>
public static class AuthSystemConstants
{
    /// <summary>
    /// Authentication scheme name for JWT Bearer tokens.
    /// </summary>
    public const string BearerScheme = "Bearer";

    /// <summary>
    /// Authentication scheme name for API Key authentication.
    /// </summary>
    public const string ApiKeyScheme = "ApiKey";

    /// <summary>
    /// Authentication scheme name for Webhook Key authentication.
    /// </summary>
    public const string WebhookKeyScheme = "WebhookKey";

    /// <summary>
    /// The HTTP header name for API key authentication.
    /// </summary>
    public const string ApiKeyHeaderName = "X-Api-Key";

    /// <summary>
    /// The query parameter name for webhook key authentication.
    /// </summary>
    public const string WebhookKeyQueryParam = "whk";

    /// <summary>
    /// The HTTP header name for gateway token authentication.
    /// </summary>
    public const string GatewayTokenHeaderName = "X-Gateway-Token";

    /// <summary>
    /// Named HTTP client for AuthSystem API calls.
    /// </summary>
    public const string HttpClientName = "AuthSystem";

    /// <summary>
    /// Named HTTP client for the machine token request. It carries the gateway token and nothing
    /// else: no user's bearer token is ever attached to it.
    /// </summary>
    public const string MachineHttpClientName = "AuthSystem.Machine";

    /// <summary>The token endpoint, relative to <see cref="AuthSystemOptions.BaseUrl"/>.</summary>
    public const string TokenEndpointPath = "/api/v1/auth/token";
}
