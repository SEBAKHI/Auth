using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.Administration.Contracts;
using Auth_API.Modules.Administration.Filters;
using Auth.Application.Interfaces;
using Auth.Application.Features.Secrets.DeleteCustomSecret;
using Auth.Application.Features.Secrets.GenerateGatewayToken;
using Auth.Application.Features.Secrets.GenerateHmacKey;
using Auth.Application.Features.Secrets.GenerateRsaKey;
using Auth.Application.Features.Secrets.GetSecretStatus;
using Auth.Application.Features.Secrets.ImportGatewayToken;
using Auth.Application.Features.Secrets.ImportHmacKey;
using Auth.Application.Features.Secrets.ImportRsaKey;
using Auth.Application.Features.Secrets.SetCustomSecret;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Administration.Controllers;

/// <summary>
/// Administrative endpoints for secret management.
/// Only available when SecretManagement:EnableAdminApi is true.
/// All endpoints require the configured permission (default: secrets.manage).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/[controller]")]
[Produces("application/json")]
[Authorize]
[RequireAdminApiEnabled]
public class SecretsController : ApiController
{
    private readonly ISender _sender;

    public SecretsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets the status of all configured secrets.
    /// Does not return actual secret values, only whether they are configured.
    /// </summary>
    /// <response code="200">Returns the secret configuration status</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpGet("status")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(SecretStatusResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetSecretStatusQuery(), cancellationToken);

        return result.Match(
            status => Ok(status),
            errors => Problem(errors));
    }

    /// <summary>
    /// Regenerates the RSA key pair used for JWT signing.
    /// WARNING: This will invalidate ALL existing access tokens immediately.
    /// Users will need to re-authenticate.
    /// </summary>
    /// <response code="200">Returns the new public key</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpPost("generate/rsa")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(RsaKeyGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateRsaKey(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(new GenerateRsaKeyCommand(userId), cancellationToken);

        return result.Match(
            publicKeyPem => Ok(new RsaKeyGenerationResponse
            {
                Success = true,
                Message = LocalizeMessage(
                    "Secrets.RsaRegenerated",
                    "RSA key pair regenerated successfully. All existing access tokens are now invalid. Users must re-authenticate."),
                PublicKeyPem = publicKeyPem
            }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Regenerates the HMAC key used for refresh token hashing.
    /// WARNING: This will invalidate ALL existing refresh tokens.
    /// Users will need to re-authenticate.
    /// </summary>
    /// <response code="200">Success message</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpPost("generate/hmac")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(HmacKeyGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateHmacKey(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(new GenerateHmacKeyCommand(userId), cancellationToken);

        return result.Match(
            _ => Ok(new HmacKeyGenerationResponse
            {
                Success = true,
                Message = LocalizeMessage(
                    "Secrets.HmacRegenerated",
                    "HMAC key regenerated successfully. All existing refresh tokens are now invalid. Users must re-authenticate.")
            }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Regenerates the gateway token used for inter-service authentication.
    /// WARNING: The API Gateway must be reconfigured with the new token.
    /// </summary>
    /// <response code="200">Returns the new gateway token</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpPost("generate/gateway-token")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(GatewayTokenGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateGatewayToken(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(new GenerateGatewayTokenCommand(userId), cancellationToken);

        return result.Match(
            token => Ok(new GatewayTokenGenerationResponse
            {
                Success = true,
                Message = LocalizeMessage(
                    "Secrets.GatewayTokenRegenerated",
                    "Gateway token regenerated successfully. Update API Gateway configuration with the new token."),
                Token = token
            }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Imports a caller-supplied RSA private key for JWT signing (bring-your-own-keys).
    /// The matching public key is derived and stored automatically. Only applicable in
    /// Certificate/Dpapi storage mode (in PlainText mode, edit appsettings.Production.json).
    /// WARNING: This replaces the current signing key and invalidates ALL existing access tokens.
    /// </summary>
    /// <param name="request">The RSA private key in PEM format (PKCS#8 or PKCS#1) in the value field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns the derived public key</response>
    /// <response code="400">Invalid key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - import not supported</response>
    [HttpPost("import/rsa")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(KeyImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportRsaKey(
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(new ImportRsaKeyCommand(request.Value, userId), cancellationToken);

        return result.Match(
            publicKeyPem => Ok(new KeyImportResponse
            {
                Success = true,
                Message = LocalizeMessage(
                    "Secrets.RsaImported",
                    "RSA signing key imported successfully. All existing access tokens are now invalid. Users must re-authenticate."),
                PublicKeyPem = publicKeyPem
            }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Imports a caller-supplied HMAC key for refresh token hashing (bring-your-own-keys).
    /// Only applicable in Certificate/Dpapi storage mode.
    /// WARNING: This replaces the current key and invalidates ALL existing refresh tokens.
    /// </summary>
    /// <param name="request">The base64-encoded HMAC key (>= 32 bytes) in the value field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Success message</response>
    /// <response code="400">Invalid key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - import not supported</response>
    [HttpPost("import/hmac")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(KeyImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportHmacKey(
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(new ImportHmacKeyCommand(request.Value, userId), cancellationToken);

        return result.Match<IActionResult>(
            _ => Ok(new KeyImportResponse
            {
                Success = true,
                Message = LocalizeMessage(
                    "Secrets.HmacImported",
                    "HMAC key imported successfully. All existing refresh tokens are now invalid. Users must re-authenticate.")
            }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Imports a caller-supplied gateway token for inter-service authentication (bring-your-own-keys).
    /// Only applicable in Certificate/Dpapi storage mode.
    /// WARNING: The API Gateway must be reconfigured with the same token.
    /// </summary>
    /// <param name="request">The gateway token in the value field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Success message</response>
    /// <response code="400">Invalid token</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - import not supported</response>
    [HttpPost("import/gateway-token")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(typeof(KeyImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportGatewayToken(
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(new ImportGatewayTokenCommand(request.Value, userId), cancellationToken);

        return result.Match<IActionResult>(
            _ => Ok(new KeyImportResponse
            {
                Success = true,
                Message = LocalizeMessage(
                    "Secrets.GatewayTokenImported",
                    "Gateway token imported successfully. Update the API Gateway configuration with the same token.")
            }),
            errors => Problem(errors));
    }

    /// <summary>
    /// Sets a custom secret value.
    /// Custom secrets are stored under the Custom namespace in the secret file.
    /// </summary>
    /// <param name="key">The secret key (alphanumeric, underscores, dots only)</param>
    /// <param name="request">The secret value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <response code="204">Secret set successfully</response>
    /// <response code="400">Invalid key format</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpPut("custom/{key}")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetCustomSecret(
        string key,
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new SetCustomSecretCommand(key, request.Value, userId),
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Deletes a custom secret.
    /// </summary>
    /// <param name="key">The secret key to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <response code="204">Secret deleted successfully</response>
    /// <response code="404">Secret not found</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpDelete("custom/{key}")]
    [RequirePermission("secrets.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteCustomSecret(
        string key,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new DeleteCustomSecretCommand(key, userId),
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
