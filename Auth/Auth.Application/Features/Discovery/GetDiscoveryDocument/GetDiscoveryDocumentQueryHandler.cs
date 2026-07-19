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

        // authorization_endpoint, response types, PKCE, id_token signing and scopes are
        // intentionally NOT advertised: the authorization-code flow does not exist yet,
        // and the document must only advertise capabilities that are actually implemented.
        // Re-add them together with the /auth/authorize endpoint (Phase 4).
        var document = new DiscoveryDocumentDto
        {
            Issuer = _jwtSettings.Issuer,
            JwksUri = $"{baseUrl}/.well-known/jwks.json",
            TokenEndpoint = $"{baseUrl}/api/{apiVersion}/auth/login",
            UserinfoEndpoint = $"{baseUrl}/api/{apiVersion}/auth/me",
            EndSessionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/logout",
            RevocationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/revoke",
            IntrospectionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/introspect",
            ResponseTypesSupported = [],
            SubjectTypesSupported = ["public"],
            // Login and refresh authenticate the user, not an OAuth client; per
            // RFC 8414 omitting this field would imply client_secret_basic.
            TokenEndpointAuthMethodsSupported = ["none"],
            ClaimsSupported = ["sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss"],
            GrantTypesSupported = ["password", "refresh_token"]
        };

        return Task.FromResult<ErrorOr<DiscoveryDocumentDto>>(document);
    }
}
