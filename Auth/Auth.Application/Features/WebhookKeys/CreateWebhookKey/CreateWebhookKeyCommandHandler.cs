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
    private readonly IApplicationRepository _applicationRepository;
    private readonly IWebhookKeyGenerator _webhookKeyGenerator;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateWebhookKeyCommandHandler> _logger;

    public CreateWebhookKeyCommandHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IApplicationRepository applicationRepository,
        IWebhookKeyGenerator webhookKeyGenerator,
        IPublisher publisher,
        ILogger<CreateWebhookKeyCommandHandler> logger)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _applicationRepository = applicationRepository;
        _webhookKeyGenerator = webhookKeyGenerator;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<CreateWebhookKeyResponse>> Handle(
        CreateWebhookKeyCommand request,
        CancellationToken cancellationToken)
    {
        // Validate application exists
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return Error.NotFound(
                code: "Application.NotFound",
                description: "The specified application was not found.");
        }

        var (webhookKeyValue, keyPrefix, keyHash) = _webhookKeyGenerator.Generate(request.Environment);

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
}
