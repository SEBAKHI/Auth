using Auth.Application.Configuration;
using Auth.Domain.Constants;
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
        // authorization-code + PKCE flow on /auth/authorize + /auth/token, the
        // scopes /auth/authorize grants (each application only those an
        // administrator allowed it), and UserInfo for an application's token.
        // id_token signing stays absent until OIDC id_tokens exist.
        var document = new DiscoveryDocumentDto
        {
            Issuer = _jwtSettings.Issuer,
            JwksUri = $"{baseUrl}/.well-known/jwks.json",
            AuthorizationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/authorize",
            TokenEndpoint = $"{baseUrl}/api/{apiVersion}/auth/token",
            // The OIDC UserInfo endpoint, which takes an application's access token. Not
            // /auth/me: that one serves the platform's own apps and refuses every application token.
            UserinfoEndpoint = $"{baseUrl}/api/{apiVersion}/auth/userinfo",
            // Not /auth/logout: that one is POST + bearer, which no browser
            // navigation can satisfy. A relying party following the spec was
            // answered 405 or 401 by an address this very document told it to use.
            EndSessionEndpoint = $"{baseUrl}/api/{apiVersion}/auth/end-session",
            RevocationEndpoint = $"{baseUrl}/api/{apiVersion}/auth/revoke",
            // No introspection_endpoint: /auth/introspect serves the platform's own callers
            // (it needs a platform token), and an application, a public client with no
            // secret, could never call it. Listing it promised every application a failure.
            ResponseTypesSupported = ["code"],
            SubjectTypesSupported = ["public"],
            // Public clients with mandatory PKCE — no client authentication at
            // the token endpoint; per RFC 8414 omitting this field would imply
            // client_secret_basic.
            TokenEndpointAuthMethodsSupported = ["none"],
            ScopesSupported = OAuthScopes.Supported,
            // The access token's claims, then what UserInfo adds for the granted scopes.
            ClaimsSupported =
            [
                "sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss",
                JwtClaimNames.GivenName, JwtClaimNames.FamilyName, JwtClaimNames.Locale, JwtClaimNames.ZoneInfo,
                JwtClaimNames.Picture, JwtClaimNames.EmailVerified, JwtClaimNames.PhoneNumber,
                JwtClaimNames.PhoneNumberVerified
            ],
            GrantTypesSupported = ["authorization_code", "refresh_token"],
            CodeChallengeMethodsSupported = ["S256"],
            // "create": OIDC Initiating User Registration 1.0 — registration
            // instead of sign-in when the browser has no session.
            PromptValuesSupported = ["login", "none", "create"]
        };

        return Task.FromResult<ErrorOr<DiscoveryDocumentDto>>(document);
    }
}
