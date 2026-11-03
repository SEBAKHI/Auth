using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Secrets.Common;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.RequestSecretOperationChallenge;

/// <summary>
/// Handler for raising a step-up confirmation. Refuses everything it can refuse
/// before a code is emailed: an operation the storage mode cannot perform, or
/// key material that would be rejected at execution anyway.
/// </summary>
public class RequestSecretOperationChallengeCommandHandler
    : IRequestHandler<RequestSecretOperationChallengeCommand, ErrorOr<SecretOperationChallengeDto>>
{
    private readonly SecretOperationChallengeService _challengeService;
    private readonly KeyringApprovals _keyrings;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<RequestSecretOperationChallengeCommandHandler> _logger;

    public RequestSecretOperationChallengeCommandHandler(
        SecretOperationChallengeService challengeService,
        KeyringApprovals keyrings,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<RequestSecretOperationChallengeCommandHandler> logger)
    {
        _challengeService = challengeService;
        _keyrings = keyrings;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<SecretOperationChallengeDto>> Handle(
        RequestSecretOperationChallengeCommand request,
        CancellationToken cancellationToken)
    {
        var isImport = request.Operation is SecretOperation.ImportRsaKey
            or SecretOperation.ImportHmacKey
            or SecretOperation.ImportGatewayToken;

        // Fail the storage-mode check here rather than after the round trip
        // through the administrator's mailbox — the import handler enforces it
        // again, this only stops us wasting a code on an operation that cannot run.
        if (isImport && _settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        if (isImport)
        {
            var material = ValidateMaterial(request.Operation, request.Value ?? string.Empty);
            if (material.IsError)
            {
                return material.Errors;
            }
        }

        // Generates carry no payload; binding one would make the digest a
        // constant and the check meaningless. A keyring change is bound to its action
        // and target instead (KR3).
        string? payloadHash;
        if (request.Action is { } action)
        {
            var bound = await KeyringDigestAsync(action, request, cancellationToken);
            if (bound.IsError)
            {
                return bound.Errors;
            }

            payloadHash = bound.Value;
        }
        else
        {
            payloadHash = isImport ? SecretPayloadDigest.Compute(request.Value) : null;
        }

        var issued = await _challengeService.IssueAsync(
            request.Operation,
            payloadHash,
            request.RequestedBy,
            request.IpAddress,
            cancellationToken,
            request.Action);

        if (issued.IsError)
        {
            return issued.Errors;
        }

        await _publisher.Publish(
            new SecretOperationChallengeIssuedEvent(
                issued.Value.ChallengeId,
                request.Operation,
                request.RequestedBy,
                request.IpAddress),
            cancellationToken);

        _logger.LogInformation(
            "Step-up confirmation {ChallengeId} raised for secret operation {Operation} ({KeyringAction})",
            issued.Value.ChallengeId, request.Operation, request.Action);

        return new SecretOperationChallengeDto
        {
            ChallengeId = issued.Value.ChallengeId,
            ExpiresAt = issued.Value.ExpiresAt,
            MaskedEmail = issued.Value.MaskedEmail
        };
    }

    // The digest the approval of a keyring change is bound to, from the ring the secrets
    // file holds now, by the binding of the ring the operation confirms changes of.
    // Everything that would refuse the change is refused there, before a code is emailed: a
    // storage mode with no file to keep the ring in, a ring the action cannot apply to.
    private async Task<ErrorOr<string>> KeyringDigestAsync(
        KeyringAction action,
        RequestSecretOperationChallengeCommand request,
        CancellationToken cancellationToken)
    {
        if (_keyrings.For(request.Operation) is not { } keyring)
        {
            return SecretErrors.OperationInvalid;
        }

        return await keyring.DigestAsync(
            action, new KeyringTarget(request.KeyId, request.Kid), request.Value, cancellationToken);
    }

    private static ErrorOr<Success> ValidateMaterial(SecretOperation operation, string value) => operation switch
    {
        SecretOperation.ImportRsaKey => SecretKeyMaterial.ValidateRsaPrivateKey(value).Match<ErrorOr<Success>>(
            _ => Result.Success,
            errors => errors),
        SecretOperation.ImportHmacKey => SecretKeyMaterial.ValidateHmacKey(value),
        _ => SecretKeyMaterial.ValidateGatewayToken(value)
    };
}
