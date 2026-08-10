using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportRsaKey;

/// <summary>
/// Handler for importing a caller-supplied RSA signing key. Validates the PEM, derives the
/// public key, and persists both to the encrypted secrets file — but only after spending a
/// confirmation bound to a digest of this exact key material, so the bytes that were approved
/// are the bytes that get stored.
/// </summary>
public class ImportRsaKeyCommandHandler : IRequestHandler<ImportRsaKeyCommand, ErrorOr<string>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<ImportRsaKeyCommandHandler> _logger;

    public ImportRsaKeyCommandHandler(
        IDpapiSecretService secretService,
        SecretOperationChallengeService challengeService,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<ImportRsaKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _challengeService = challengeService;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> Handle(
        ImportRsaKeyCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        var derived = SecretKeyMaterial.ValidateRsaPrivateKey(request.PrivateKeyPem);
        if (derived.IsError)
        {
            return derived.Errors;
        }

        var publicKeyPem = derived.Value;

        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            SecretOperation.ImportRsaKey,
            SecretPayloadDigest.Compute(request.PrivateKeyPem),
            request.RequestedBy,
            cancellationToken);

        if (approval.IsError)
        {
            return approval.Errors;
        }

        try
        {
            _logger.LogWarning(
                "RSA signing key import requested by user {UserId} - all existing access tokens will be invalidated",
                request.RequestedBy);

            await _secretService.ImportRsaKeyPairAsync(request.PrivateKeyPem, publicKeyPem, cancellationToken);

            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId, SecretOperation.ImportRsaKey, request.RequestedBy),
                cancellationToken);

            return publicKeyPem;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during RSA key import");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during RSA key import");
            return SecretErrors.FileAccessFailed;
        }
    }
}
