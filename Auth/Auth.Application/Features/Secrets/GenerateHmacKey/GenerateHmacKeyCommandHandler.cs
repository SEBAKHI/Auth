using System.Security.Cryptography;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateHmacKey;

/// <summary>
/// Handler for regenerating the HMAC key. Spends the step-up confirmation
/// first: nothing touches the secret store until the approval is proven and
/// consumed.
/// </summary>
public class GenerateHmacKeyCommandHandler : IRequestHandler<GenerateHmacKeyCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly IPublisher _publisher;
    private readonly ILogger<GenerateHmacKeyCommandHandler> _logger;

    public GenerateHmacKeyCommandHandler(
        IDpapiSecretService secretService,
        SecretOperationChallengeService challengeService,
        IPublisher publisher,
        ILogger<GenerateHmacKeyCommandHandler> logger)
    {
        _secretService = secretService;
        _challengeService = challengeService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        GenerateHmacKeyCommand request,
        CancellationToken cancellationToken)
    {
        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            SecretOperation.GenerateHmacKey,
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
                "HMAC key regeneration requested by user {UserId} - all refresh tokens will be invalidated",
                request.RequestedBy);

            await _secretService.GenerateHmacKeyAsync(cancellationToken);

            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId, SecretOperation.GenerateHmacKey, request.RequestedBy),
                cancellationToken);

            return Result.Success;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to generate HMAC key");
            return SecretErrors.KeyGenerationFailed;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during HMAC key generation");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during HMAC key generation");
            return SecretErrors.FileAccessFailed;
        }
    }
}
