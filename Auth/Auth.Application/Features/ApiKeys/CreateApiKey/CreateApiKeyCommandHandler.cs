using Auth.Application.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
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
    private readonly PermissionGrantGuard _grantGuard;
    private readonly IPublisher _publisher;
    private readonly ILogger<CreateApiKeyCommandHandler> _logger;

    public CreateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IApplicationRepository applicationRepository,
        IPermissionRepository permissionRepository,
        IApiKeyGenerator apiKeyGenerator,
        PermissionGrantGuard grantGuard,
        IPublisher publisher,
        ILogger<CreateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _applicationRepository = applicationRepository;
        _permissionRepository = permissionRepository;
        _apiKeyGenerator = apiKeyGenerator;
        _grantGuard = grantGuard;
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

        // Resolve every requested scope before anything is written, and refuse an id that does not
        // resolve rather than dropping it: silently skipping a mistyped id used to mint a key with
        // fewer scopes than the caller asked for, with nothing in the response to say so.
        var requestedScopes = new Dictionary<Guid, string>();
        foreach (var permissionId in request.PermissionIds ?? [])
        {
            var permission = await _permissionRepository.GetByIdAsync(permissionId, cancellationToken);
            if (permission is null)
            {
                return PermissionErrors.NotFound(permissionId);
            }

            requestedScopes[permissionId] = permission.Code.Value;
        }

        // No amplification. An API key is a credential in its own right, carried by relying parties
        // through the SDK, so scoping one past what its creator holds hands out authority that
        // creator could not have granted directly - and it leaves this system's trust boundary. The
        // user and role grant paths already ask this question; the key path did not.
        if (requestedScopes.Count > 0)
        {
            var canGrant = await _grantGuard.EnsureCanGrantAsync(
                request.CreatedBy, requestedScopes.Values, cancellationToken);
            if (canGrant.IsError)
            {
                _logger.LogWarning(
                    "Blocked API key creation for application {ApplicationId}: actor {CreatedBy} does not hold every requested scope",
                    request.ApplicationId, request.CreatedBy);
                return canGrant.Errors;
            }
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

        foreach (var permissionId in requestedScopes.Keys)
        {
            var scope = ApiKeyScope.Create(apiKey.Id, permissionId, request.CreatedBy);
            await _apiKeyRepository.AddScopeAsync(scope, cancellationToken);
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
