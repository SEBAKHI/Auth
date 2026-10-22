using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Auth.Domain.Enums;
using Auth.Domain.Events;

namespace Auth.Application.Features.WebhookKeys.ValidateWebhookKey;

/// <summary>
/// Handler for validating a webhook key using deterministic HMAC-SHA256 hash lookup.
/// </summary>
/// <remarks>
/// A key whose stored fingerprint was computed under an HMAC key that no longer issues
/// (S11) still validates through that key's 90-day grace, and its first successful use
/// asks for it to be re-fingerprinted under the issuing key: the event's handler
/// rewrites the row, conditionally, and nothing it does can fail this validation.
/// </remarks>
public class ValidateWebhookKeyQueryHandler : IRequestHandler<ValidateWebhookKeyQuery, ErrorOr<ValidateWebhookKeyResponse>>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IPublisher _publisher;
    private readonly ILogger<ValidateWebhookKeyQueryHandler> _logger;

    public ValidateWebhookKeyQueryHandler(
        IWebhookKeyRepository webhookKeyRepository,
        ICredentialHasher credentialHasher,
        IPublisher publisher,
        ILogger<ValidateWebhookKeyQueryHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _credentialHasher = credentialHasher;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<ValidateWebhookKeyResponse>> Handle(
        ValidateWebhookKeyQuery request,
        CancellationToken cancellationToken)
    {
        var candidates = _credentialHasher.Candidates(CredentialPurpose.WebhookKey, request.RawWebhookKey);

        var webhookKey = await _webhookKeyRepository.GetByHashAsync(candidates, cancellationToken);
        if (webhookKey is null)
        {
            _logger.LogWarning("Webhook key validation failed: key not found");
            return WebhookKeyErrors.Invalid;
        }

        await _webhookKeyRepository.RecordUsageAsync(webhookKey.Id, cancellationToken);

        var issuing = _credentialHasher.Hash(CredentialPurpose.WebhookKey, request.RawWebhookKey);
        if (!string.Equals(webhookKey.KeyHash, issuing, StringComparison.Ordinal))
        {
            // The validation has succeeded; the re-fingerprint must not undo that, so it
            // is the event's handler's, which never throws, and it is not cancelled with
            // the caller's request.
            await _publisher.Publish(
                new WebhookKeyHashUpgradedEvent(webhookKey.Id, webhookKey.KeyHash, issuing),
                CancellationToken.None);
        }

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
