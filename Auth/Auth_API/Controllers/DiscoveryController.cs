using Asp.Versioning;
using Auth_API.Common;
using Auth.Application.Configuration;
using Auth.Application.Features.Discovery.GetDiscoveryDocument;
using Auth.Application.Features.Discovery.GetJwks;
using Auth.Application.Features.Discovery.GetPublicKey;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Controllers;

/// <summary>
/// OpenID Connect Discovery endpoints for external token validation.
/// These endpoints follow OIDC specification and are NOT versioned.
/// </summary>
[ApiController]
[ApiVersionNeutral]  // Excludes from API versioning - OIDC spec requires fixed paths
[AllowAnonymous]
// Gateway-token exempt and anonymous, so it shares the process-wide public-surface
// concurrency ceiling with /health and /ready. See the policy in Program.cs.
[EnableRateLimiting("public-surface")]
public class DiscoveryController : ApiController
{
    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;

    public DiscoveryController(ISender sender, IOptionsSnapshot<IdentityProviderSettings> idpSettings)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
    }

    /// <summary>
    /// OpenID Connect Discovery Document.
    /// </summary>
    [HttpGet(".well-known/openid-configuration")]
    [ProducesResponseType(typeof(DiscoveryDocumentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpenIdConfiguration(CancellationToken cancellationToken)
    {
        // Build endpoint URLs from the configured public origin, not Request.Host:
        // behind the gateway the host is the internal destination, which would
        // publish internal URLs (authorization_endpoint, jwks_uri, ...) to every
        // OIDC client. Falls back to the request host in proxy-less dev.
        var baseUrl = _idpSettings.ResolvePublicBaseUrl($"{Request.Scheme}://{Request.Host}");
        var result = await _sender.Send(new GetDiscoveryDocumentQuery(baseUrl), cancellationToken);

        return result.Match(
            document => Ok(document),
            errors => Problem(errors));
    }

    /// <summary>
    /// JSON Web Key Set (JWKS) for token validation.
    /// </summary>
    [HttpGet(".well-known/jwks.json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("application/json")]
    public async Task<IActionResult> GetJwks(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetJwksQuery(), cancellationToken);

        return result.Match(
            jwks => Content(jwks, "application/json"),
            errors => Problem(errors));
    }

    /// <summary>
    /// Public key in PEM format.
    /// </summary>
    [HttpGet(".well-known/public-key.pem")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [Produces("text/plain")]
    public async Task<IActionResult> GetPublicKey(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPublicKeyQuery(), cancellationToken);

        return result.Match(
            pem => Content(pem, "text/plain"),
            errors => Problem(errors));
    }
}
