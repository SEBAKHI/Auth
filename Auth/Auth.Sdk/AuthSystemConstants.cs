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

    /// <summary>
    /// Named HTTP client for the credential checks (SDK 2.0): API key and webhook key validation
    /// and token introspection. It carries the gateway token, and each request the machine access
    /// token; never a user's token from the token store.
    /// </summary>
    public const string MachineApiHttpClientName = "AuthSystem.MachineApi";

    /// <summary>The token endpoint, relative to <see cref="AuthSystemOptions.BaseUrl"/>.</summary>
    public const string TokenEndpointPath = "/api/v1/auth/token";

    /// <summary>API key validation, asked by this application with its machine token.</summary>
    public const string ApiKeyValidationPath = "/api/v1/app/machine/apikeys/validate";

    /// <summary>Webhook key validation, asked by this application with its machine token.</summary>
    public const string WebhookKeyValidationPath = "/api/v1/app/machine/webhookkeys/validate";

    /// <summary>Token introspection (RFC 7662), asked by this application with its machine token.</summary>
    public const string TokenIntrospectionPath = "/api/v1/app/machine/tokens/introspect";

    /// <summary>
    /// The audience of every machine token the server issues, whichever application asked for it.
    /// It is never an application's own <see cref="AuthSystemOptions.Audience"/>: an application
    /// configured with it would accept any application's machine token as a caller, so
    /// <c>AddAuthSystemAuthentication</c> refuses it.
    /// </summary>
    public const string MachineTokenAudience = "urn:authsystem:machine";
}
