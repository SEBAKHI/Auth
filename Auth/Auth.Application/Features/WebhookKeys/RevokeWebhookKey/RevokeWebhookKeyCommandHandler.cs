using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.RevokeWebhookKey;

/// <summary>
/// Handler for revoking a webhook key.
/// </summary>
public class RevokeWebhookKeyCommandHandler : IRequestHandler<RevokeWebhookKeyCommand, ErrorOr<Success>>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<RevokeWebhookKeyCommandHandler> _logger;

    public RevokeWebhookKeyCommandHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IPublisher publisher,
        ILogger<RevokeWebhookKeyCommandHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RevokeWebhookKeyCommand request,
        CancellationToken cancellationToken)
    {
        var webhookKey = await _webhookKeyRepository.GetByIdAsync(request.Id, cancellationToken);
        if (webhookKey is null)
        {
            return WebhookKeyErrors.NotFound;
        }

        if (webhookKey.IsRevoked)
        {
            return WebhookKeyErrors.AlreadyRevoked;
        }

        webhookKey.Revoke(request.RevokedBy, request.Reason);
        await _webhookKeyRepository.UpdateAsync(webhookKey, cancellationToken);

        _logger.LogInformation(
            "Webhook key revoked: {WebhookKeyId} by {RevokedBy}",
            request.Id, request.RevokedBy);

        await _publisher.Publish(
            new WebhookKeyRevokedEvent(webhookKey.Id, webhookKey.ApplicationId, request.RevokedBy),
            cancellationToken);

        return Result.Success;
    }
}
