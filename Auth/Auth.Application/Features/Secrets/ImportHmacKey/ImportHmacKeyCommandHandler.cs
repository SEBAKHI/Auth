using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Handler for importing a caller-supplied HMAC key. Validates that the value is base64 and at
/// least 256 bits, then persists it to the encrypted secrets file — but only after spending a
/// confirmation bound to a digest of this exact key material.
/// </summary>
public class ImportHmacKeyCommandHandler : IRequestHandler<ImportHmacKeyCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<ImportHmacKeyCommandHandler> _logger;

    public ImportHmacKeyCommandHandler(
        IDpapiSecretService secretService,
        SecretOperationChallengeService challengeService,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<ImportHmacKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _challengeService = challengeService;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ImportHmacKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        var material = SecretKeyMaterial.ValidateHmacKey(request.HmacKeyBase64);
        if (material.IsError)
        {
            return material.Errors;
        }

        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            SecretOperation.ImportHmacKey,
            SecretPayloadDigest.Compute(request.HmacKeyBase64),
            request.RequestedBy,
            cancellationToken);

        if (approval.IsError)
        {
            return approval.Errors;
        }

        try
        {
            _logger.LogWarning(
                "HMAC key import requested by user {UserId} - all existing refresh tokens will be invalidated",
                request.RequestedBy);

            await _secretService.ImportHmacKeyAsync(request.HmacKeyBase64, cancellationToken);

            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId, SecretOperation.ImportHmacKey, request.RequestedBy),
                cancellationToken);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during HMAC key import");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during HMAC key import");
            return SecretErrors.FileAccessFailed;
        }
    }
}
