using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
public class SecretsController : ControllerBase
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretManagementSettings _settings;
    private readonly ILogger<SecretsController> _logger;

    public SecretsController(
        IDpapiSecretService secretService,
        IOptions<SecretManagementSettings> settings,
        ILogger<SecretsController> logger)
    {
        _secretService = secretService;
        _settings = settings.Value;
        _logger = logger;
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
        if (!_settings.EnableAdminApi)
        {
            return Forbid("Secret management admin API is disabled.");
        }

        var status = await _secretService.GetStatusAsync(cancellationToken);
        return Ok(status);
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
        if (!_settings.EnableAdminApi)
        {
            return Forbid("Secret management admin API is disabled.");
        }

        var userId = User.FindFirstValue("sub") ?? "unknown";
        _logger.LogWarning(
            "RSA key regeneration requested by user {UserId} - all access tokens will be invalidated",
            userId);

        var publicKeyPem = await _secretService.GenerateRsaKeyPairAsync(cancellationToken);

        return Ok(new RsaKeyGenerationResponse
        {
            Success = true,
            Message = "RSA key pair regenerated successfully. All existing access tokens are now invalid. Users must re-authenticate.",
            PublicKeyPem = publicKeyPem
        });
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
        if (!_settings.EnableAdminApi)
        {
            return Forbid("Secret management admin API is disabled.");
        }

        var userId = User.FindFirstValue("sub") ?? "unknown";
        _logger.LogWarning(
            "HMAC key regeneration requested by user {UserId} - all refresh tokens will be invalidated",
            userId);

        await _secretService.GenerateHmacKeyAsync(cancellationToken);

        return Ok(new HmacKeyGenerationResponse
        {
            Success = true,
            Message = "HMAC key regenerated successfully. All existing refresh tokens are now invalid. Users must re-authenticate."
        });
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
        if (!_settings.EnableAdminApi)
        {
            return Forbid("Secret management admin API is disabled.");
        }

        var userId = User.FindFirstValue("sub") ?? "unknown";
        _logger.LogWarning(
            "Gateway token regeneration requested by user {UserId}",
            userId);

        var token = await _secretService.GenerateGatewayTokenAsync(cancellationToken);

        return Ok(new GatewayTokenGenerationResponse
        {
            Success = true,
            Message = "Gateway token regenerated successfully. Update API Gateway configuration with the new token.",
            Token = token
        });
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
        if (!_settings.EnableAdminApi)
        {
            return Forbid("Secret management admin API is disabled.");
        }

        if (!IsValidSecretKey(key))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Key",
                Detail = "Secret key must be alphanumeric with underscores or dots only, and less than 100 characters.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var userId = User.FindFirstValue("sub") ?? "unknown";
        await _secretService.SetSecretAsync($"Custom:{key}", request.Value, cancellationToken);

        _logger.LogInformation(
            "Custom secret {Key} set by user {UserId}",
            key,
            userId);

        return NoContent();
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
        if (!_settings.EnableAdminApi)
        {
            return Forbid("Secret management admin API is disabled.");
        }

        var removed = await _secretService.RemoveSecretAsync($"Custom:{key}", cancellationToken);

        if (!removed)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Secret Not Found",
                Detail = $"Custom secret '{key}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var userId = User.FindFirstValue("sub") ?? "unknown";
        _logger.LogInformation(
            "Custom secret {Key} deleted by user {UserId}",
            key,
            userId);

        return NoContent();
    }

    private static bool IsValidSecretKey(string key)
    {
        return !string.IsNullOrWhiteSpace(key) &&
               key.Length <= 100 &&
               key.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Response DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Response for RSA key generation.
/// </summary>
public record RsaKeyGenerationResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The public key in PEM format for external token validation.
    /// </summary>
    public string PublicKeyPem { get; init; } = string.Empty;
}

/// <summary>
/// Response for HMAC key generation.
/// </summary>
public record HmacKeyGenerationResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Response for gateway token generation.
/// </summary>
public record GatewayTokenGenerationResponse
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The generated gateway token. Store this securely - it will not be shown again.
    /// </summary>
    public string Token { get; init; } = string.Empty;
}

/// <summary>
/// Request for setting a secret value.
/// </summary>
public record SetSecretRequest
{
    /// <summary>
    /// The secret value to store.
    /// </summary>
    public string Value { get; init; } = string.Empty;
}
