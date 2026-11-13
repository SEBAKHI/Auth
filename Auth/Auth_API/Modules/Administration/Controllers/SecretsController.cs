using System.Security.Claims;
using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.Administration.Contracts;
using Auth_API.Modules.Administration.Filters;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Features.Secrets.DeleteCustomSecret;
using Auth.Application.Features.Secrets.DiscardStagedHmacKey;
using Auth.Application.Features.Secrets.DiscardStagedSigningKey;
using Auth.Application.Features.Secrets.GenerateGatewayToken;
using Auth.Application.Features.Secrets.GenerateHmacKey;
using Auth.Application.Features.Secrets.GenerateRsaKey;
using Auth.Application.Features.Secrets.GetHmacKeyring;
using Auth.Application.Features.Secrets.GetSecretStatus;
using Auth.Application.Features.Secrets.GoogleClientSecret;
using Auth.Application.Features.Secrets.TurnstileSecret;
using Auth.Application.Features.Secrets.ImportGatewayToken;
using Auth.Application.Features.Secrets.ImportHmacKey;
using Auth.Application.Features.Secrets.ImportRsaKey;
using Auth.Application.Features.Secrets.PromoteHmacKey;
using Auth.Application.Features.Secrets.PromoteSigningKey;
using Auth.Application.Features.Secrets.ReinstateHmacKey;
using Auth.Application.Features.Secrets.RequestSecretOperationChallenge;
using Auth.Application.Features.Secrets.RetireHmacKey;
using Auth.Application.Features.Secrets.RetireSigningKey;
using Auth.Application.Features.Secrets.SetConnectionString;
using Auth.Application.Features.Secrets.SetCustomSecret;
using Auth.Application.Features.Secrets.SetSmtpPassword;
using Auth.Application.Features.Secrets.StageHmacKey;
using Auth.Application.Features.Secrets.StageSigningKey;
using Auth.Application.Features.Secrets.VerifySecretOperationChallenge;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Auth.Domain.Constants;

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
    [RequirePermission(PermissionCodes.Secrets.Manage)]
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
    /// Raises a step-up confirmation for a destructive secret operation: emails a
    /// one-time code to the requesting administrator. Nothing is rotated here.
    /// </summary>
    /// <remarks>
    /// For the import operations the key material must be supplied now, not only
    /// at execution: the confirmation is bound to a digest of it, so an approval
    /// cannot be obtained for one key and spent on another.
    /// </remarks>
    /// <param name="request">The operation to confirm, and its key material for imports.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Confirmation raised; the code was emailed</response>
    /// <response code="400">Invalid operation or key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or too many codes requested</response>
    /// <response code="409">No confirmed email to send a code to, or storage mode is PlainText</response>
    [HttpPost("challenges")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(SecretOperationChallengeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestOperationChallenge(
        [FromBody] SecretOperationChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new RequestSecretOperationChallengeCommand(
                request.Operation, request.Value, userId, GetClientIpAddress(), request.KeyringAction, request.KeyId, request.Kid),
            cancellationToken);

        return result.Match(
            challenge => Ok(challenge),
            errors => Problem(errors));
    }

    /// <summary>
    /// Answers a step-up confirmation with the emailed code. On success the
    /// approval window opens and the operation's blast radius is returned — the
    /// live figures the administrator confirms against.
    /// </summary>
    /// <param name="challengeId">The challenge being answered.</param>
    /// <param name="request">The six-digit code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Code accepted; returns the rotation impact</response>
    /// <response code="400">The code is incorrect or no longer valid</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpPost("challenges/{challengeId:guid}/verify")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(SecretRotationImpactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> VerifyOperationChallenge(
        Guid challengeId,
        [FromBody] VerifySecretOperationChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new VerifySecretOperationChallengeCommand(
                challengeId, request.Code, userId, request.KeyringAction, request.KeyId, request.Value, request.Kid),
            cancellationToken);

        return result.Match(
            impact => Ok(impact),
            errors => Problem(errors));
    }

    /// <summary>
    /// Regenerates the RSA key pair used for JWT signing: an alias of keyring/rsa/stage and an
    /// emergency promote, in one change (S12), kept for consoles cached before the signing
    /// keyring until SUNSET(S12). WARNING: the new key signs once the API restarts, and from then
    /// on every access token the old key signed is refused; users obtain fresh tokens.
    /// A rotation that signs nobody out is keyring/rsa/stage, a restart, then keyring/rsa/promote.
    /// Requires a verified confirmation raised for this exact operation.
    /// </summary>
    /// <param name="request">The verified confirmation authorizing the rotation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns the new public key</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed operation</response>
    [HttpPost("generate/rsa")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(RsaKeyGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateRsaKey(
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new GenerateRsaKeyCommand(request.ChallengeId, userId), cancellationToken);

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
    /// Replaces the HMAC key every opaque credential is fingerprinted under, at once: an
    /// alias of an emergency promote of a generated key (S11), kept for consoles cached
    /// before the keyring until SUNSET(S11). WARNING: refresh tokens, SSO sessions, reset
    /// and invitation links, emailed codes and sign-ins in flight stop working now; webhook
    /// keys keep validating for 90 days and are re-fingerprinted on their next use.
    /// A scheduled rotation that signs nobody out is keyring/hmac/stage then promote.
    /// Requires a verified confirmation raised for this exact operation.
    /// </summary>
    /// <param name="request">The verified confirmation authorizing the rotation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Success message</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed operation</response>
    [HttpPost("generate/hmac")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateHmacKey(
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new GenerateHmacKeyCommand(request.ChallengeId, userId), cancellationToken);

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
    /// The HMAC keyring (S11): which key issues, which is staged, and how long each
    /// older key keeps verifying, in the secrets file and in this process. Never key
    /// material. Its <c>rsa</c> member is the signing keyring (S12): the same, by kid, and
    /// when it cannot be managed here, why, with the key's source.
    /// </summary>
    /// <response code="200">The keyring</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText, or the HMAC key is not kept in the secrets file</response>
    [HttpGet("keyring")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetKeyring(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetHmacKeyringQuery(), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Stages a new HMAC key, generated or supplied: it verifies from now on and issues
    /// nothing until it is promoted, so nothing breaks. Requires a verified confirmation
    /// raised for this action (keyringAction Stage, or StageImport with the value).
    /// </summary>
    /// <param name="request">The verified confirmation, and the key to stage if you supply one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The keyring after the change</response>
    /// <response code="400">Invalid key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">The keyring cannot be managed on this deployment</response>
    [HttpPost("keyring/hmac/stage")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StageHmacKey(
        [FromBody] KeyringStageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new StageHmacKeyCommand(request.Value, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Promotes an HMAC key. Scheduled: the staged key issues from now on, and the old
    /// one keeps verifying through its overlap window, so nobody is signed out. Emergency:
    /// a new key issues at once and every older key stops verifying now, except for
    /// webhook keys. Requires a verified confirmation raised for this action
    /// (Promote or PromoteEmergency).
    /// </summary>
    /// <param name="request">The verified confirmation, and whether this is an emergency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">No staged key loaded in this process, or the keyring cannot be managed here</response>
    [HttpPost("keyring/hmac/promote")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PromoteHmacKey(
        [FromBody] KeyringPromoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new PromoteHmacKeyCommand(request.Emergency, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes the staged HMAC key before it is promoted. Nothing was issued under it.
    /// Requires a verified confirmation raised for this action (DiscardNext).
    /// </summary>
    /// <param name="request">The verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">No staged key, or the keyring cannot be managed here</response>
    [HttpPost("keyring/hmac/discard-next")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardStagedHmacKey(
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DiscardStagedHmacKeyCommand(request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Makes a previous HMAC key current again: the rollback of a promote. Requires a
    /// verified confirmation raised for this action and this key (Reinstate, keyId).
    /// </summary>
    /// <param name="id">The previous key to reinstate.</param>
    /// <param name="request">The verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">No such previous key still in its window, or the keyring cannot be managed here</response>
    [HttpPost("keyring/hmac/reinstate/{id:int}")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReinstateHmacKey(
        int id,
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new ReinstateHmacKeyCommand(id, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes a previous HMAC key for good; the secrets file is saved twice, so its backup
    /// copy loses the key too. Refused while live webhook keys stored under it are still in
    /// their grace. Requires a verified confirmation raised for this action and this key
    /// (Retire, keyId).
    /// </summary>
    /// <param name="id">The previous key to retire.</param>
    /// <param name="request">The verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">Not a previous key, webhook keys still need it, or the keyring cannot be managed here</response>
    [HttpPost("keyring/hmac/retire/{id:int}")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(HmacKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireHmacKey(
        int id,
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RetireHmacKeyCommand(id, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Stages a new signing key, generated or supplied (S12): the API publishes and accepts it
    /// from its next restart, and nothing signs with it until it is promoted, so nothing breaks.
    /// Restart the API next, so this process loads the key and records when it published it.
    /// Requires a verified confirmation raised for this action (keyringAction Stage with the
    /// operation GenerateRsaKey, or StageImport with ImportRsaKey and the value).
    /// </summary>
    /// <param name="request">The verified confirmation, and the private key to stage if you supply one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The signing keyring after the change</response>
    /// <response code="400">Invalid key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">The key is in use or was used before, or the signing keyring cannot be managed here</response>
    [HttpPost("keyring/rsa/stage")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(SigningKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StageSigningKey(
        [FromBody] KeyringStageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new StageSigningKeyCommand(request.Value, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Promotes the staged signing key (S12). Scheduled: it signs from now on, and the old key is
    /// still accepted for about a day, so nobody is signed out; it needs the staged key loaded in
    /// this process (a restart after the stage) and published for 24 hours. Emergency, after a
    /// leak: it signs at once and the old key is neither accepted nor published; that takes effect
    /// here at once when this process loaded the staged key, otherwise at the next restart
    /// (<c>changeAppliedInProcess</c> says which). Requires a verified confirmation raised for this
    /// action (Promote or PromoteEmergency).
    /// </summary>
    /// <param name="request">The verified confirmation, and whether this is an emergency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The signing keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">No staged key loaded here, published too recently, or the signing keyring cannot be managed here</response>
    [HttpPost("keyring/rsa/promote")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(SigningKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PromoteSigningKey(
        [FromBody] KeyringPromoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new PromoteSigningKeyCommand(request.Emergency, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes the staged signing key before it is promoted (S12). Nothing was signed with it.
    /// An API process that already loaded it keeps publishing and accepting it until it restarts.
    /// Requires a verified confirmation raised for this action (DiscardNext).
    /// </summary>
    /// <param name="request">The verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The signing keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">No staged key, or the signing keyring cannot be managed here</response>
    [HttpPost("keyring/rsa/discard-next")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(SigningKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DiscardStagedSigningKey(
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DiscardStagedSigningKeyCommand(request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes a previous signing key, or a compromised record, for good (S12); the secrets file
    /// is saved twice, so its backup copy loses the key too. A key still in its window stops being
    /// accepted now. Requires a verified confirmation raised for this action and this key
    /// (Retire, kid).
    /// </summary>
    /// <param name="kid">The key to retire.</param>
    /// <param name="request">The verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">The signing keyring after the change</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed change</response>
    /// <response code="409">The current or the staged key, no such key, or the signing keyring cannot be managed here</response>
    [HttpPost("keyring/rsa/retire/{kid}")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(SigningKeyringDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireSigningKey(
        string kid,
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new RetireSigningKeyCommand(kid, request.ChallengeId, GetUserId()), cancellationToken);

        return result.Match(
            keyring => Ok(keyring),
            errors => Problem(errors));
    }

    /// <summary>
    /// Regenerates the gateway token used for inter-service authentication.
    /// WARNING: The API Gateway must be reconfigured with the new token; until it
    /// is, every proxied request is rejected.
    /// Requires a verified confirmation raised for this exact operation.
    /// </summary>
    /// <param name="request">The verified confirmation authorizing the rotation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns the new gateway token</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed operation</response>
    [HttpPost("generate/gateway-token")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(GatewayTokenGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GenerateGatewayToken(
        [FromBody] ConfirmedSecretOperationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new GenerateGatewayTokenCommand(request.ChallengeId, userId), cancellationToken);

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
    /// Imports a caller-supplied RSA private key for JWT signing (bring-your-own-keys): an alias of
    /// keyring/rsa/stage with that key and an emergency promote, in one change (S12), kept until
    /// SUNSET(S12). The matching public key is derived and stored automatically. Only applicable in
    /// Certificate/Dpapi storage mode (in PlainText mode, edit appsettings.Production.json).
    /// A key the signing keyring holds, or held before, is refused.
    /// WARNING: the key signs once the API restarts, and from then on ALL existing access tokens are refused.
    /// </summary>
    /// <param name="request">The RSA private key in PEM format (PKCS#8 or PKCS#1), plus the verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Returns the derived public key</response>
    /// <response code="400">Invalid key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed operation</response>
    /// <response code="409">Storage mode is PlainText - import not supported</response>
    [HttpPost("import/rsa")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(KeyImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportRsaKey(
        [FromBody] ImportSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new ImportRsaKeyCommand(request.Value, request.ChallengeId, userId), cancellationToken);

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
    /// Imports a caller-supplied HMAC key (bring-your-own-keys): an alias of an emergency
    /// promote of that key (S11), kept until SUNSET(S11). Only applicable in
    /// Certificate/Dpapi storage mode. WARNING: as for generate/hmac, everything computed
    /// under the old keys stops working now, except webhook keys (90-day grace).
    /// </summary>
    /// <param name="request">The base64-encoded HMAC key (>= 32 bytes), plus the verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Success message</response>
    /// <response code="400">Invalid key material</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed operation</response>
    /// <response code="409">Storage mode is PlainText - import not supported</response>
    [HttpPost("import/hmac")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(KeyImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportHmacKey(
        [FromBody] ImportSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new ImportHmacKeyCommand(request.Value, request.ChallengeId, userId), cancellationToken);

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
    /// <param name="request">The gateway token, plus the verified confirmation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Success message</response>
    /// <response code="400">Invalid token</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions, admin API disabled, or unconfirmed operation</response>
    /// <response code="409">Storage mode is PlainText - import not supported</response>
    [HttpPost("import/gateway-token")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(typeof(KeyImportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ImportGatewayToken(
        [FromBody] ImportSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new ImportGatewayTokenCommand(request.Value, request.ChallengeId, userId), cancellationToken);

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
    /// Stores the SMTP password in the encrypted secrets file, where it overrides
    /// <c>Email:Password</c> from configuration. Takes effect on the next API restart.
    /// </summary>
    /// <remarks>
    /// No step-up confirmation, unlike the rotations above: the confirmation code
    /// is delivered by email, so requiring one to repair a broken SMTP password
    /// would mean the code can only arrive after the fix it authorizes.
    /// </remarks>
    /// <param name="request">The SMTP password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Password stored; restart the API to apply it</response>
    /// <response code="400">Missing or over-long value</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - there is no encrypted file to write to</response>
    [HttpPut("smtp-password")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetSmtpPassword(
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new SetSmtpPasswordCommand(request.Value, userId),
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Stores Google's OAuth client secret in the encrypted secrets file, where it becomes
    /// <c>ExternalAuth:Google:ClientSecret</c>: the server's half of the Google sign-in's code
    /// exchange. Takes effect on the next API restart.
    /// </summary>
    /// <remarks>
    /// No step-up confirmation, like the SMTP password and the connection string: the value is
    /// Google's, transcribed here, and a wrong one only makes the Google sign-in fail until it
    /// is corrected. The status reports whether it is set, never the value.
    /// </remarks>
    /// <param name="request">The client secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Secret stored; restart the API to apply it</response>
    /// <response code="400">Missing or over-long value</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - there is no encrypted file to write to</response>
    [HttpPut("google-client-secret")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetGoogleClientSecret(
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SetGoogleClientSecretCommand(request.Value, GetUserId()),
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes Google's OAuth client secret from the encrypted secrets file. Takes effect on the
    /// next API restart; from then on the Google sign-in's code exchange answers
    /// <c>ExternalAuth.ProviderNotConfigured</c> until a secret is set again.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Secret removed; restart the API to apply it</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - there is no encrypted file to write to</response>
    [HttpDelete("google-client-secret")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClearGoogleClientSecret(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ClearGoogleClientSecretCommand(GetUserId()), cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Stores Cloudflare Turnstile's secret key in the encrypted secrets file (S13), where it becomes
    /// <c>BotDefense:TurnstileSecret</c>: the server's half of the bot check on the sign-in pages.
    /// Takes effect on the next API restart.
    /// </summary>
    /// <remarks>
    /// No step-up confirmation, like the Google client secret: the value is Cloudflare's,
    /// transcribed here, and a wrong one only makes the check unable to judge. In Production one of
    /// Cloudflare's testing secrets, which accept every token, is refused. The status reports
    /// whether it is set, never the value.
    /// </remarks>
    /// <param name="request">The secret key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Secret stored; restart the API to apply it</response>
    /// <response code="400">Missing or over-long value, or a testing secret in Production</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - there is no encrypted file to write to</response>
    [HttpPut("turnstile-secret")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetTurnstileSecret(
        [FromBody] SetSecretRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new SetTurnstileSecretCommand(request.Value, GetUserId()),
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Removes Cloudflare Turnstile's secret key from the encrypted secrets file (S13). Takes effect
    /// on the next API restart; from then on, with a sitekey still set, the check cannot judge and
    /// the fail mode decides.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Secret removed; restart the API to apply it</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - there is no encrypted file to write to</response>
    [HttpDelete("turnstile-secret")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClearTurnstileSecret(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ClearTurnstileSecretCommand(GetUserId()), cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Stores the AuthDb connection string in the encrypted secrets file, where it
    /// overrides <c>ConnectionStrings:AuthDb</c> from configuration — including the
    /// environment variables supplied by web.config. Takes effect on the next API restart.
    /// </summary>
    /// <remarks>
    /// The value is probed before it is stored. A string that cannot be parsed is
    /// refused outright. A string that parses but cannot reach the server is
    /// refused once, and stored only if the caller resubmits with
    /// <c>forceSave</c> — which is how a database password is rotated, since the
    /// new credential is not live at the server yet.
    /// </remarks>
    /// <param name="request">The connection string, and whether to store it despite a failed connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="204">Connection string stored; restart the API to apply it</response>
    /// <response code="400">The value is malformed, or unreachable without forceSave</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    /// <response code="409">Storage mode is PlainText - there is no encrypted file to write to</response>
    [HttpPut("connection-string")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetConnectionString(
        [FromBody] SetConnectionStringRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var result = await _sender.Send(
            new SetConnectionStringCommand(request.Value, request.ForceSave, userId),
            cancellationToken);

        return result.Match<IActionResult>(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Sets a custom secret value.
    /// Custom secrets are stored under the Custom namespace in the secret file.
    /// </summary>
    /// <remarks>
    /// Custom secrets are namespaced: the key is stored under <c>Custom:</c> and
    /// surfaces as <c>Secrets:Custom:{key}</c>. This endpoint therefore cannot set
    /// a first-class secret — passing <c>ConnectionStrings.AuthDb</c> here stores a
    /// value nothing reads. Use the dedicated endpoints above for those.
    /// </remarks>
    /// <param name="key">The secret key (alphanumeric, underscores, dots only)</param>
    /// <param name="request">The secret value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <response code="204">Secret set successfully</response>
    /// <response code="400">Invalid key format</response>
    /// <response code="401">Unauthorized - not authenticated</response>
    /// <response code="403">Forbidden - insufficient permissions or admin API disabled</response>
    [HttpPut("custom/{key}")]
    [RequirePermission(PermissionCodes.Secrets.Manage)]
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
    [RequirePermission(PermissionCodes.Secrets.Manage)]
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
