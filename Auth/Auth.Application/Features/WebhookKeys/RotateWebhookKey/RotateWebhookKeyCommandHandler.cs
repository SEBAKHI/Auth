using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.RotateWebhookKey;

/// <summary>
/// Handler for rotating a webhook key.
/// </summary>
public class RotateWebhookKeyCommandHandler : IRequestHandler<RotateWebhookKeyCommand, ErrorOr<RotateWebhookKeyResponse>>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IWebhookKeyGenerator _webhookKeyGenerator;
    private readonly ILogger<RotateWebhookKeyCommandHandler> _logger;

    public RotateWebhookKeyCommandHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IWebhookKeyGenerator webhookKeyGenerator,
        ILogger<RotateWebhookKeyCommandHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _webhookKeyGenerator = webhookKeyGenerator;
        _logger = logger;
    }

    public async Task<ErrorOr<RotateWebhookKeyResponse>> Handle(
        RotateWebhookKeyCommand request,
        CancellationToken cancellationToken)
    {
        var existingKey = await _webhookKeyRepository.GetByIdAsync(request.WebhookKeyId, cancellationToken);
        if (existingKey is null)
        {
            return WebhookKeyErrors.NotFound;
        }

        if (existingKey.IsRevoked)
        {
            return WebhookKeyErrors.Revoked;
        }

        // Generate new webhook key using shared generator
        var (newKeyValue, newKeyPrefix, newKeyHash) = _webhookKeyGenerator.Generate(existingKey.Environment);

        var newWebhookKey = WebhookKey.Create(
            applicationId: existingKey.ApplicationId,
            name: $"{existingKey.Name} (rotated)",
            keyPrefix: newKeyPrefix,
            keyHash: newKeyHash,
            targetUrl: existingKey.TargetUrl,
            createdBy: request.RotatedBy,
            description: $"Rotated from webhook key {existingKey.Id}",
            environment: existingKey.Environment);

        await _webhookKeyRepository.CreateAsync(newWebhookKey, cancellationToken);

        // Schedule expiration on the old key so it becomes invalid after the grace period
        DateTime? oldKeyExpiresAt = null;
        if (request.GracePeriodMinutes > 0)
        {
            oldKeyExpiresAt = DateTime.UtcNow.AddMinutes(request.GracePeriodMinutes);
            existingKey.ScheduleExpiration(oldKeyExpiresAt.Value);
            await _webhookKeyRepository.UpdateAsync(existingKey, cancellationToken);
        }

        _logger.LogInformation(
            "Webhook key rotated: old {OldWebhookKeyId} -> new {NewWebhookKeyId} by {RotatedBy}",
            request.WebhookKeyId, newWebhookKey.Id, request.RotatedBy);

        return new RotateWebhookKeyResponse
        {
            NewWebhookKey = newKeyValue,
            NewWebhookKeyId = newWebhookKey.Id,
            NewKeyPrefix = newKeyPrefix,
            OldKeyExpiresAt = oldKeyExpiresAt,
            OldWebhookKeyId = existingKey.Id,
            Message = request.GracePeriodMinutes > 0
                ? $"New webhook key created. Old key remains valid for {request.GracePeriodMinutes} minutes."
                : "New webhook key created. Old key should be revoked manually."
        };
    }
}
