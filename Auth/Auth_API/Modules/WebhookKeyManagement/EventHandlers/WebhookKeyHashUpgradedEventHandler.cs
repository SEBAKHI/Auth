using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;

namespace Auth_API.Modules.WebhookKeyManagement.EventHandlers;

/// <summary>
/// Re-fingerprints a webhook key that validated under an HMAC key that no longer issues
/// (S11 D-T3, owner decision §11 q2), so it keeps working after that key's 90-day grace
/// without its integrator doing anything.
/// </summary>
/// <remarks>
/// Best-effort: a re-fingerprint that does not happen leaves the key validating through
/// the grace, and its next use asks again. The rewrite is conditional on the fingerprint
/// the validation matched, so a concurrent re-fingerprint, a rotation or a revoke that got
/// there first wins. It never throws: MediatR's publisher hands a handler's exception to
/// the caller, and the caller is a validation that has already succeeded.
/// </remarks>
public class WebhookKeyHashUpgradedEventHandler : INotificationHandler<WebhookKeyHashUpgradedEvent>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly ILogger<WebhookKeyHashUpgradedEventHandler> _logger;

    public WebhookKeyHashUpgradedEventHandler(
        IWebhookKeyRepository webhookKeyRepository,
        ILogger<WebhookKeyHashUpgradedEventHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _logger = logger;
    }

    public async Task Handle(WebhookKeyHashUpgradedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var upgraded = await _webhookKeyRepository.UpgradeKeyHashAsync(
                notification.WebhookKeyId, notification.MatchedHash, notification.NewHash, cancellationToken);

            if (upgraded)
            {
                _logger.LogInformation(
                    "Webhook key {WebhookKeyId} re-fingerprinted under the current HMAC key",
                    notification.WebhookKeyId);
            }
            else
            {
                _logger.LogDebug(
                    "Webhook key {WebhookKeyId} was not re-fingerprinted: its stored fingerprint changed first",
                    notification.WebhookKeyId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "Re-fingerprinting webhook key {WebhookKeyId} failed; it keeps validating through its grace and is retried on its next use",
                notification.WebhookKeyId);
        }
    }
}
