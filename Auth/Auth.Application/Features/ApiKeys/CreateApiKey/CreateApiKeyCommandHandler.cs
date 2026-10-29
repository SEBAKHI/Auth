using Auth.Application.Features.ApiKeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
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
    private readonly IApiKeyGenerator _apiKeyGenerator;
    private readonly ApiKeyScopePolicy _scopePolicy;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateApiKeyCommandHandler> _logger;

    public CreateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IApplicationRepository applicationRepository,
        IApiKeyGenerator apiKeyGenerator,
        ApiKeyScopePolicy scopePolicy,
        IPublisher publisher,
        ILogger<CreateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _applicationRepository = applicationRepository;
        _apiKeyGenerator = apiKeyGenerator;
        _scopePolicy = scopePolicy;
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
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var allowedIps = IpAllowList.Create(request.AllowedIps);
        if (allowedIps.IsError)
        {
            return allowedIps.Errors;
        }

        // At least one scope, each the application's own active permission, and each held by the
        // creator (OI-128 PR B).
        var scopes = await _scopePolicy.ResolveAsync(
            request.ApplicationId, request.PermissionIds ?? [], request.CreatedBy, cancellationToken);
        if (scopes.IsError)
        {
            _logger.LogWarning(
                "Blocked API key creation for application {ApplicationId} by {CreatedBy}: {Code}",
                request.ApplicationId, request.CreatedBy, scopes.FirstError.Code);
            return scopes.Errors;
        }

        var generated = _apiKeyGenerator.Generate(request.Environment);

        var apiKey = ApiKey.Create(
            applicationId: request.ApplicationId,
            name: request.Name,
            keyPrefix: generated.KeyPrefix,
            keySelector: generated.KeySelector,
            keyHash: generated.KeyHash,
            createdBy: request.CreatedBy,
            description: request.Description,
            environment: request.Environment,
            allowedIps: allowedIps.Value,
            expiresAt: request.ExpiresAt);

        // The key and its scopes in one transaction: a failing scope leaves no key behind.
        await _apiKeyRepository.CreateAsync(
            apiKey,
            scopes.Value.Select(permissionId => ApiKeyScope.Create(apiKey.Id, permissionId, request.CreatedBy)).ToList(),
            cancellationToken);

        _logger.LogInformation(
            "API key created: {ApiKeyId} for application {ApplicationId} by {CreatedBy}",
            apiKey.Id, request.ApplicationId, request.CreatedBy);

        await _publisher.Publish(
            new ApiKeyCreatedEvent(apiKey.Id, request.ApplicationId, request.Name, request.CreatedBy),
            cancellationToken);

        return new CreateApiKeyResponse
        {
            Id = apiKey.Id,
            ApiKey = generated.PlainKey,  // Return the actual key - only time it's visible
            KeyPrefix = generated.KeyPrefix,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt
        };
    }
}
