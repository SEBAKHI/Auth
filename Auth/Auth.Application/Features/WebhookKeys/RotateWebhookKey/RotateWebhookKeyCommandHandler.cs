using System.Security.Cryptography;
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
    private readonly IWebhookKeyHasher _webhookKeyHasher;
    private readonly ILogger<RotateWebhookKeyCommandHandler> _logger;

    public RotateWebhookKeyCommandHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IWebhookKeyHasher webhookKeyHasher,
        ILogger<RotateWebhookKeyCommandHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _webhookKeyHasher = webhookKeyHasher;
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

        // Generate new webhook key
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var randomPart = Convert.ToBase64String(randomBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..32];

        var newKeyValue = $"{existingKey.KeyPrefix}{randomPart}";
        var newKeyHash = _webhookKeyHasher.ComputeHash(newKeyValue);

        var newWebhookKey = WebhookKey.Create(
            applicationId: existingKey.ApplicationId,
            name: $"{existingKey.Name} (rotated)",
            keyPrefix: existingKey.KeyPrefix,
            keyHash: newKeyHash,
            targetUrl: existingKey.TargetUrl,
            createdBy: request.RotatedBy,
            description: $"Rotated from webhook key {existingKey.Id}",
            environment: existingKey.Environment,
            expiresAt: existingKey.ExpiresAt);

        await _webhookKeyRepository.CreateAsync(newWebhookKey, cancellationToken);

        // Calculate grace period expiration for old key
        DateTime? oldKeyExpiresAt = null;
        if (request.GracePeriodMinutes > 0)
        {
            oldKeyExpiresAt = DateTime.UtcNow.AddMinutes(request.GracePeriodMinutes);
        }

        _logger.LogInformation(
            "Webhook key rotated: old {OldWebhookKeyId} -> new {NewWebhookKeyId} by {RotatedBy}",
            request.WebhookKeyId, newWebhookKey.Id, request.RotatedBy);

        return new RotateWebhookKeyResponse
        {
            NewWebhookKey = newKeyValue,
            NewWebhookKeyId = newWebhookKey.Id,
            NewKeyPrefix = existingKey.KeyPrefix,
            OldKeyExpiresAt = oldKeyExpiresAt,
            OldWebhookKeyId = existingKey.Id,
            Message = request.GracePeriodMinutes > 0
                ? $"New webhook key created. Old key remains valid for {request.GracePeriodMinutes} minutes."
                : "New webhook key created. Old key should be revoked manually."
        };
    }
}
