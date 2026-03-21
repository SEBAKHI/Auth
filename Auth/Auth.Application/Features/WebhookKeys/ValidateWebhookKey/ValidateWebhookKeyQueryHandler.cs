using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// Handler for validating a webhook key using deterministic HMAC-SHA256 hash lookup.
/// </summary>
public class ValidateWebhookKeyQueryHandler : IRequestHandler<ValidateWebhookKeyQuery, ErrorOr<ValidateWebhookKeyResponse>>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IWebhookKeyHasher _webhookKeyHasher;
    private readonly ILogger<ValidateWebhookKeyQueryHandler> _logger;

    public ValidateWebhookKeyQueryHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IWebhookKeyHasher webhookKeyHasher,
        ILogger<ValidateWebhookKeyQueryHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _webhookKeyHasher = webhookKeyHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<ValidateWebhookKeyResponse>> Handle(
        ValidateWebhookKeyQuery request,
        CancellationToken cancellationToken)
    {
        var keyHash = _webhookKeyHasher.ComputeHash(request.RawWebhookKey);

        var webhookKey = await _webhookKeyRepository.GetByHashAsync(keyHash, cancellationToken);
        if (webhookKey is null)
        {
            _logger.LogWarning("Webhook key validation failed: key not found");
            return WebhookKeyErrors.Invalid;
        }

        await _webhookKeyRepository.RecordUsageAsync(webhookKey.Id, cancellationToken);

        _logger.LogInformation(
            "Webhook key validated: {WebhookKeyId} for application {ApplicationId}",
            webhookKey.Id, webhookKey.ApplicationId);

        return new ValidateWebhookKeyResponse
        {
            Active = true,
            WebhookKeyId = webhookKey.Id,
            ApplicationId = webhookKey.ApplicationId,
            Name = webhookKey.Name,
            TargetUrl = webhookKey.TargetUrl,
            Environment = webhookKey.Environment
        };
    }
}
