using System.Security.Cryptography;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateGatewayToken;

/// <summary>
/// Handler for regenerating the gateway token. Spends the step-up confirmation
/// first: nothing touches the secret store until the approval is proven and
/// consumed.
/// </summary>
public class GenerateGatewayTokenCommandHandler : IRequestHandler<GenerateGatewayTokenCommand, ErrorOr<string>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly IPublisher _publisher;
    private readonly ILogger<GenerateGatewayTokenCommandHandler> _logger;

    public GenerateGatewayTokenCommandHandler(
        IDpapiSecretService secretService,
        SecretOperationChallengeService challengeService,
        IPublisher publisher,
        ILogger<GenerateGatewayTokenCommandHandler> logger)
    {
        _secretService = secretService;
        _challengeService = challengeService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<string>> Handle(
        GenerateGatewayTokenCommand request,
        CancellationToken cancellationToken)
    {
        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            SecretOperation.GenerateGatewayToken,
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
                "Gateway token regeneration requested by user {UserId}",
                request.RequestedBy);

            var token = await _secretService.GenerateGatewayTokenAsync(cancellationToken);

            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId, SecretOperation.GenerateGatewayToken, request.RequestedBy),
                cancellationToken);

            return token;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to generate gateway token");
            return SecretErrors.KeyGenerationFailed;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during gateway token generation");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during gateway token generation");
            return SecretErrors.FileAccessFailed;
        }
    }
}
