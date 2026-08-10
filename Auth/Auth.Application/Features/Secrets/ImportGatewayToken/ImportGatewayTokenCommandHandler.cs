using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Secrets.ImportGatewayToken;

/// <summary>
/// Handler for importing a caller-supplied gateway token, persisting it to the encrypted
/// secrets file — but only after spending a confirmation bound to a digest of this exact token.
/// </summary>
public class ImportGatewayTokenCommandHandler : IRequestHandler<ImportGatewayTokenCommand, ErrorOr<Success>>
{
    private readonly IDpapiSecretService _secretService;
    private readonly SecretOperationChallengeService _challengeService;
    private readonly SecretManagementSettings _settings;
    private readonly IPublisher _publisher;
    private readonly ILogger<ImportGatewayTokenCommandHandler> _logger;

    public ImportGatewayTokenCommandHandler(
        IDpapiSecretService secretService,
        SecretOperationChallengeService challengeService,
        IOptions<SecretManagementSettings> settings,
        IPublisher publisher,
        ILogger<ImportGatewayTokenCommandHandler> logger)
    {
        _secretService = secretService;
        _challengeService = challengeService;
        _settings = settings.Value;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        ImportGatewayTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (_settings.IsPlainTextMode)
        {
            return SecretErrors.ImportNotSupportedInPlainText;
        }

        var material = SecretKeyMaterial.ValidateGatewayToken(request.Token);
        if (material.IsError)
        {
            return material.Errors;
        }

        var approval = await _challengeService.ConsumeAsync(
            request.ChallengeId,
            SecretOperation.ImportGatewayToken,
            SecretPayloadDigest.Compute(request.Token),
            request.RequestedBy,
            cancellationToken);

        if (approval.IsError)
        {
            return approval.Errors;
        }

        try
        {
            _logger.LogWarning(
                "Gateway token import requested by user {UserId} - the API Gateway must be reconfigured with the same token",
                request.RequestedBy);

            await _secretService.ImportGatewayTokenAsync(request.Token, cancellationToken);

            await _publisher.Publish(
                new SecretOperationExecutedEvent(
                    request.ChallengeId, SecretOperation.ImportGatewayToken, request.RequestedBy),
                cancellationToken);

            return Result.Success;
        }
        catch (SecretDecryptionException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file during gateway token import");
            return SecretErrors.DecryptionFailed;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to save secret file during gateway token import");
            return SecretErrors.FileAccessFailed;
        }
    }
}
