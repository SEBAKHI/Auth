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
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<RequestSecretOperationChallengeCommandHandler> _logger;

    public RequestSecretOperationChallengeCommandHandler(
        SecretOperationChallengeService challengeService,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<RequestSecretOperationChallengeCommandHandler> logger)
    {
        _challengeService = challengeService;
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
        // constant and the check meaningless.
        var payloadHash = isImport ? SecretPayloadDigest.Compute(request.Value) : null;

        var issued = await _challengeService.IssueAsync(
            request.Operation,
            payloadHash,
            request.RequestedBy,
            request.IpAddress,
            cancellationToken);

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
            "Step-up confirmation {ChallengeId} raised for secret operation {Operation}",
            issued.Value.ChallengeId, request.Operation);

        return new SecretOperationChallengeDto
        {
            ChallengeId = issued.Value.ChallengeId,
            ExpiresAt = issued.Value.ExpiresAt,
            MaskedEmail = issued.Value.MaskedEmail
        };
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
