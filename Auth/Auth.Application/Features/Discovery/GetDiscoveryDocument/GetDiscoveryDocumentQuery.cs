using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Discovery.GetDiscoveryDocument;

/// <summary>
/// Query to retrieve the OpenID Connect Discovery Document.
/// </summary>
public record GetDiscoveryDocumentQuery(string BaseUrl) : IRequest<ErrorOr<DiscoveryDocumentDto>>;

/// <summary>
/// OpenID Connect Discovery Document response.
/// </summary>
public record DiscoveryDocumentDto
{
    public required string Issuer { get; init; }
    public required string JwksUri { get; init; }
    public string? AuthorizationEndpoint { get; init; }
    public required string TokenEndpoint { get; init; }
    public string? UserinfoEndpoint { get; init; }
    public string? EndSessionEndpoint { get; init; }
    public string? RevocationEndpoint { get; init; }
    public string? IntrospectionEndpoint { get; init; }
    public IReadOnlyList<string> ResponseTypesSupported { get; init; } = [];
    public IReadOnlyList<string> SubjectTypesSupported { get; init; } = [];
    public IReadOnlyList<string> IdTokenSigningAlgValuesSupported { get; init; } = [];
    public IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; init; } = [];
    public IReadOnlyList<string> ScopesSupported { get; init; } = [];
    public IReadOnlyList<string> ClaimsSupported { get; init; } = [];
    public IReadOnlyList<string> GrantTypesSupported { get; init; } = [];
    public IReadOnlyList<string> CodeChallengeMethodsSupported { get; init; } = [];
}
