using System.Security.Cryptography;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateRsaKey;

/// <summary>
/// Handler for regenerating the RSA key pair. Spends the step-up confirmation
/// first: nothing touches the secret store until the approval is proven and
/// consumed.
/// </summary>
public class GenerateRsaKeyCommandHandler : IRequestHandler<GenerateRsaKeyCommand, ErrorOr<string>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly IPublisher _publisher;
    private readonly ILogger<GenerateRsaKeyCommandHandler> _logger;

    public GenerateRsaKeyCommandHandler(
        IDpapiSecretService secretService,
        SecretOperationChallengeService challengeService,
        IPublisher publisher,
        ILogger<GenerateRsaKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _challengeService = challengeService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> Handle(
        GenerateRsaKeyCommand request,
        CancellationToken cancellationToken)
    {
        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            SecretOperation.GenerateRsaKey,
            payloadHash: null,
            request.RequestedBy,
            cancellationToken);

        if (approval.IsError)
        {
            return approval.Errors;
        }

        try
        {
            _logger.LogWarning(
                "RSA key regeneration requested by user {UserId} - all access tokens will be invalidated",
                request.RequestedBy);

            var publicKeyPem = await _secretService.GenerateRsaKeyPairAsync(cancellationToken);

            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId, SecretOperation.GenerateRsaKey, request.RequestedBy),
                cancellationToken);

            return publicKeyPem;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to generate RSA key pair");
            return SecretErrors.KeyGenerationFailed;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during RSA key generation");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during RSA key generation");
            return SecretErrors.FileAccessFailed;
        }
    }
}
