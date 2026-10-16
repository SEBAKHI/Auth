using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.MachineKeys.CreateMachineKey;
using Auth.Application.Features.MachineKeys.GetMachineKeys;
using Auth.Application.Features.MachineKeys.RevokeMachineKey;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.ApplicationManagement.Controllers;

/// <summary>
/// An application's machine keys (OI-128): the credentials its own server uses at the token endpoint
/// (the <c>client_credentials</c> grant with <c>private_key_jwt</c>).
/// </summary>
/// <remarks>
/// Guarded by the API-key permissions. A machine key opens only the machine endpoints, and only for
/// its own application, so it is no stronger a credential than an API key of that application.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/applications/{applicationId:guid}/machine-keys")]
[Authorize]
public class ApplicationMachineKeysController : ApiController
{
    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;

    public ApplicationMachineKeysController(ISender sender, IOptionsSnapshot<IdentityProviderSettings> idpSettings)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
    }

    /// <summary>
    /// List the application's machine keys, revoked ones included. Never returns key material.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionCodes.ApiKeys.Read)]
    [ProducesResponseType(typeof(IReadOnlyList<MachineKeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMachineKeys(Guid applicationId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetMachineKeysQuery(applicationId), cancellationToken);

        return result.Match(
            keys => Ok(keys),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create a machine key: the server generates an ES256 pair and returns the private key once,
    /// or stores the public JWK sent in the body.
    /// </summary>
    [HttpPost]
    [RequirePermission(PermissionCodes.ApiKeys.Create)]
    [ProducesResponseType(typeof(CreateMachineKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMachineKey(
        Guid applicationId,
        [FromBody] CreateMachineKeyRequest request,
        CancellationToken cancellationToken)
    {
        // The token endpoint the answer names is the discovery document's, from the same origin.
        var publicBaseUrl = _idpSettings.ResolvePublicBaseUrl($"{Request.Scheme}://{Request.Host}");
        var command = new CreateMachineKeyCommand(
            applicationId, request.Name, request.PublicKeyJwk, publicBaseUrl, request.Purpose ?? MachineKeyPurpose.Machine)
        {
            CreatedBy = GetCurrentUserId()
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            created =>
            {
                // The private key is in this body: no cache, anywhere on the way back.
                Response.Headers.CacheControl = "no-store";
                return CreatedAtAction(nameof(GetMachineKeys), new { applicationId }, created);
            },
            errors => Problem(errors));
    }

    /// <summary>
    /// Revoke a machine key. Tokens it already obtained expire within five minutes.
    /// </summary>
    [HttpPost("{keyId:guid}/revoke")]
    [RequirePermission(PermissionCodes.ApiKeys.Revoke)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevokeMachineKey(Guid applicationId, Guid keyId, CancellationToken cancellationToken)
    {
        var command = new RevokeMachineKeyCommand(applicationId, keyId) { RevokedBy = GetCurrentUserId() };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }
}
