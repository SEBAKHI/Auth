using System.Text.Json.Serialization;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Discovery.GetDiscoveryDocument;

/// <summary>
/// Query to retrieve the OpenID Connect Discovery Document.
/// </summary>
public record GetDiscoveryDocumentQuery(string BaseUrl) : IRequest<ErrorOr<DiscoveryDocumentDto>>;

/// <summary>
/// OpenID Connect Discovery Document response.
/// Property names are pinned to the exact RFC 8414 / OIDC Discovery metadata names:
/// standard clients (including Auth.Sdk's JwtBearer metadata retriever) only recognize
/// the snake_case names, so the API's global camelCase policy must not apply here.
/// Nullable members are omitted from the response while the capability is unimplemented.
/// </summary>
public record DiscoveryDocumentDto
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public string? AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("userinfo_endpoint")]
    public string? UserinfoEndpoint { get; init; }

    [JsonPropertyName("end_session_endpoint")]
    public string? EndSessionEndpoint { get; init; }

    [JsonPropertyName("revocation_endpoint")]
    public string? RevocationEndpoint { get; init; }

    [JsonPropertyName("introspection_endpoint")]
    public string? IntrospectionEndpoint { get; init; }

    [JsonPropertyName("response_types_supported")]
    public IReadOnlyList<string> ResponseTypesSupported { get; init; } = [];

    [JsonPropertyName("subject_types_supported")]
    public IReadOnlyList<string> SubjectTypesSupported { get; init; } = [];

    [JsonPropertyName("id_token_signing_alg_values_supported")]
    public IReadOnlyList<string>? IdTokenSigningAlgValuesSupported { get; init; }

    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; init; } = [];

    [JsonPropertyName("scopes_supported")]
    public IReadOnlyList<string>? ScopesSupported { get; init; }

    [JsonPropertyName("claims_supported")]
    public IReadOnlyList<string> ClaimsSupported { get; init; } = [];

    [JsonPropertyName("grant_types_supported")]
    public IReadOnlyList<string> GrantTypesSupported { get; init; } = [];

    [JsonPropertyName("code_challenge_methods_supported")]
    public IReadOnlyList<string>? CodeChallengeMethodsSupported { get; init; }

    /// <summary>
    /// The prompt values the authorize endpoint honours. Advertised only now that
    /// both are actually enforced server-side: <c>login</c> demands a fresh
    /// authentication and verifies one happened, and <c>none</c> returns
    /// <c>login_required</c> instead of redirecting to a login page a silent
    /// request cannot display. <c>consent</c> and <c>select_account</c> stay
    /// unlisted because there is no screen behind either.
    /// </summary>
    [JsonPropertyName("prompt_values_supported")]
    public IReadOnlyList<string>? PromptValuesSupported { get; init; }
}
