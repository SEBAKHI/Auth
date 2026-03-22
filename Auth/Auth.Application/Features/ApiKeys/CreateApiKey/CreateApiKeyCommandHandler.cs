using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.CreateApiKey;

/// <summary>
/// Handler for creating a new API key.
/// </summary>
public class CreateApiKeyCommandHandler : IRequestHandler<CreateApiKeyCommand, ErrorOr<CreateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IApiKeyGenerator _apiKeyGenerator;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateApiKeyCommandHandler> _logger;

    public CreateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IApplicationRepository applicationRepository,
        IPermissionRepository permissionRepository,
        IApiKeyGenerator apiKeyGenerator,
        IPublisher publisher,
        ILogger<CreateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _applicationRepository = applicationRepository;
        _permissionRepository = permissionRepository;
        _apiKeyGenerator = apiKeyGenerator;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<CreateApiKeyResponse>> Handle(
        CreateApiKeyCommand request,
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

        // Generate the API key using Argon2id
        var (apiKeyValue, keyPrefix, keyHash) = _apiKeyGenerator.Generate(request.Environment);

        // Create the API key entity
        var apiKey = ApiKey.Create(
            applicationId: request.ApplicationId,
            name: request.Name,
            keyPrefix: keyPrefix,
            keyHash: keyHash,
            createdBy: request.CreatedBy,
            description: request.Description,
            environment: request.Environment,
            rateLimitPerMinute: request.RateLimitPerMinute,
            rateLimitPerDay: request.RateLimitPerDay,
            expiresAt: request.ExpiresAt);

        await _apiKeyRepository.CreateAsync(apiKey, cancellationToken);

        // Add permission scopes if provided
        if (request.PermissionIds != null && request.PermissionIds.Count > 0)
        {
            foreach (var permissionId in request.PermissionIds)
            {
                var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
                if (permission != null)
                {
                    var scope = ApiKeyScope.Create(apiKey.Id, permissionId, request.CreatedBy);
                    await _apiKeyRepository.AddScopeAsync(scope, cancellationToken);
                }
            }
        }

        _logger.LogInformation(
            "API key created: {ApiKeyId} for application {ApplicationId} by {CreatedBy}",
            apiKey.Id, request.ApplicationId, request.CreatedBy);

        await _publisher.Publish(
            new ApiKeyCreatedEvent(apiKey.Id, request.ApplicationId, request.Name, request.CreatedBy),
            cancellationToken);

        return new CreateApiKeyResponse
        {
            Id = apiKey.Id,
            ApiKey = apiKeyValue,  // Return the actual key - only time it's visible
            KeyPrefix = keyPrefix,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt
        };
    }
}
