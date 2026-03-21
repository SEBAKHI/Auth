using System.Security.Cryptography;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.CreateWebhookKey;

/// <summary>
/// Handler for creating a new webhook key.
/// </summary>
public class CreateWebhookKeyCommandHandler : IRequestHandler<CreateWebhookKeyCommand, ErrorOr<CreateWebhookKeyResponse>>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IWebhookKeyHasher _webhookKeyHasher;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateWebhookKeyCommandHandler> _logger;

    public CreateWebhookKeyCommandHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IWebhookKeyHasher webhookKeyHasher,
        IPublisher publisher,
        ILogger<CreateWebhookKeyCommandHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _webhookKeyHasher = webhookKeyHasher;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<CreateWebhookKeyResponse>> Handle(
        CreateWebhookKeyCommand request,
        CancellationToken cancellationToken)
    {
        var (webhookKeyValue, keyPrefix, keyHash) = GenerateWebhookKey(request.Environment, _webhookKeyHasher);

        var webhookKey = WebhookKey.Create(
            applicationId: request.ApplicationId,
            name: request.Name,
            keyPrefix: keyPrefix,
            keyHash: keyHash,
            targetUrl: request.TargetUrl,
            createdBy: request.CreatedBy,
            description: request.Description,
            environment: request.Environment,
            expiresAt: request.ExpiresAt);

        await _webhookKeyRepository.CreateAsync(webhookKey, cancellationToken);

        _logger.LogInformation(
            "Webhook key created: {WebhookKeyId} for application {ApplicationId} by {CreatedBy}",
            webhookKey.Id, request.ApplicationId, request.CreatedBy);

        await _publisher.Publish(
            new WebhookKeyCreatedEvent(webhookKey.Id, request.ApplicationId, request.Name, request.CreatedBy),
            cancellationToken);

        return new CreateWebhookKeyResponse
        {
            Id = webhookKey.Id,
            WebhookKey = webhookKeyValue,
            KeyPrefix = keyPrefix,
            CreatedAt = webhookKey.CreatedAt,
            ExpiresAt = webhookKey.ExpiresAt
        };
    }

    private static (string WebhookKey, string Prefix, string Hash) GenerateWebhookKey(
        string environment,
        IWebhookKeyHasher hasher)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var randomPart = Convert.ToBase64String(randomBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..32];

        var prefix = environment switch
        {
            "production" => "wk_prod_",
            "staging" => "wk_stag_",
            "development" => "wk_dev_",
            _ => "wk_"
        };

        var webhookKey = $"{prefix}{randomPart}";
        var hash = hasher.ComputeHash(webhookKey);

        return (webhookKey, prefix, hash);
    }
}
