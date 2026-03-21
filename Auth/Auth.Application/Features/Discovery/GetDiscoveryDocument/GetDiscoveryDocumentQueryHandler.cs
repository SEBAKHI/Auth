using Auth.Application.Configuration;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Discovery.GetDiscoveryDocument;

/// <summary>
/// Handles the GetDiscoveryDocumentQuery by building the OIDC discovery document.
/// </summary>
public class GetDiscoveryDocumentQueryHandler
    : IRequestHandler<GetDiscoveryDocumentQuery, ErrorOr<DiscoveryDocumentDto>>
{
    private readonly JwtSettings _jwtSettings;

    public GetDiscoveryDocumentQueryHandler(IOptions<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    public Task<ErrorOr<DiscoveryDocumentDto>> Handle(
        GetDiscoveryDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl;
        const string apiVersion = "v1";

        var document = new DiscoveryDocumentDto
        {
            Issuer = _jwtSettings.Issuer,
            JwksUri = $"{baseUrl}/.well-known/jwks.json",
            AuthorizationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/authorize",
            TokenEndpoint = $"{baseUrl}/api/{apiVersion}/auth/login",
            UserinfoEndpoint = $"{baseUrl}/api/{apiVersion}/auth/me",
            EndSessionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/logout",
            RevocationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/revoke",
            IntrospectionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/introspect",
            ResponseTypesSupported = ["code", "token", "id_token"],
            SubjectTypesSupported = ["public"],
            IdTokenSigningAlgValuesSupported = ["RS256"],
            TokenEndpointAuthMethodsSupported = ["client_secret_post", "client_secret_basic"],
            ScopesSupported = ["openid", "profile", "email", "offline_access"],
            ClaimsSupported = ["sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss"],
            GrantTypesSupported = ["password", "refresh_token"],
            CodeChallengeMethodsSupported = ["S256"]
        };

        return Task.FromResult<ErrorOr<DiscoveryDocumentDto>>(document);
    }
}
