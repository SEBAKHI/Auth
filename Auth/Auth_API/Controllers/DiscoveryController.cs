using Asp.Versioning;
using Auth_API.Common;
using Auth.Application.Features.Discovery.GetDiscoveryDocument;
using Auth.Application.Features.Discovery.GetJwks;
using Auth.Application.Features.Discovery.GetPublicKey;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Controllers;

/// <summary>
/// OpenID Connect Discovery endpoints for external token validation.
/// These endpoints follow OIDC specification and are NOT versioned.
/// </summary>
[ApiController]
[ApiVersionNeutral]  // Excludes from API versioning - OIDC spec requires fixed paths
[AllowAnonymous]
public class DiscoveryController : ApiController
{
    private readonly ISender _sender;

    public DiscoveryController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// OpenID Connect Discovery Document.
    /// </summary>
    [HttpGet(".well-known/openid-configuration")]
    [ProducesResponseType(typeof(DiscoveryDocumentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpenIdConfiguration(CancellationToken cancellationToken)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
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
