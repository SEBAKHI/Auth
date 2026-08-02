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

    public GetDiscoveryDocumentQueryHandler(IOptionsSnapshot<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    public Task<ErrorOr<DiscoveryDocumentDto>> Handle(
        GetDiscoveryDocumentQuery request,
        CancellationToken cancellationToken)
    {
        var baseUrl = request.BaseUrl;
        const string apiVersion = "v1";

        // The document advertises exactly what is implemented: the
        // authorization-code + PKCE flow on /auth/authorize + /auth/token.
        // id_token signing and scopes stay absent until OIDC id_tokens exist.
        var document = new DiscoveryDocumentDto
        {
            Issuer = _jwtSettings.Issuer,
            JwksUri = $"{baseUrl}/.well-known/jwks.json",
            AuthorizationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/authorize",
            TokenEndpoint = $"{baseUrl}/api/{apiVersion}/auth/token",
            UserinfoEndpoint = $"{baseUrl}/api/{apiVersion}/auth/me",
            EndSessionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/logout",
            RevocationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/revoke",
            IntrospectionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/introspect",
            ResponseTypesSupported = ["code"],
            SubjectTypesSupported = ["public"],
            // Public clients with mandatory PKCE — no client authentication at
            // the token endpoint; per RFC 8414 omitting this field would imply
            // client_secret_basic.
            TokenEndpointAuthMethodsSupported = ["none"],
            ClaimsSupported = ["sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss"],
            GrantTypesSupported = ["authorization_code", "refresh_token"],
            CodeChallengeMethodsSupported = ["S256"]
        };

        return Task.FromResult<ErrorOr<DiscoveryDocumentDto>>(document);
    }
}
