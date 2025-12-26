using Asp.Versioning;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth_API.Controllers;

/// <summary>
/// OpenID Connect Discovery endpoints for external token validation.
/// These endpoints follow OIDC specification and are NOT versioned.
/// </summary>
[ApiController]
[ApiVersionNeutral]  // Excludes from API versioning - OIDC spec requires fixed paths
[AllowAnonymous]
public class DiscoveryController : ControllerBase
{
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtSettings _jwtSettings;

    public DiscoveryController(
        IJwtTokenService jwtTokenService,
        IOptions<JwtSettings> jwtSettings)
    {
        _jwtTokenService = jwtTokenService;
        _jwtSettings = jwtSettings.Value;
    }

    /// <summary>
    /// OpenID Connect Discovery Document.
    /// </summary>
    [HttpGet(".well-known/openid-configuration")]
    [ProducesResponseType(typeof(OpenIdConfiguration), StatusCodes.Status200OK)]
    public IActionResult GetOpenIdConfiguration()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // API version for endpoint URLs
        const string apiVersion = "v1";

        var config = new OpenIdConfiguration
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

        return Ok(config);
    }

    /// <summary>
    /// JSON Web Key Set (JWKS) for token validation.
    /// </summary>
    [HttpGet(".well-known/jwks.json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/json")]
    public IActionResult GetJwks()
    {
        var jwks = _jwtTokenService.GetJwks();
        return Content(jwks, "application/json");
    }

    /// <summary>
    /// Public key in PEM format.
    /// </summary>
    [HttpGet(".well-known/public-key.pem")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("text/plain")]
    public IActionResult GetPublicKey()
    {
        var pem = _jwtTokenService.GetPublicKeyPem();
        return Content(pem, "text/plain");
    }
}

/// <summary>
/// OpenID Connect Discovery Document response.
/// </summary>
public record OpenIdConfiguration
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
