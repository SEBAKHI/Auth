using System.Security.Cryptography;
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.ApiKeys.CreateApiKey;

/// <summary>
/// Handler for creating a new API key.
/// </summary>
public class CreateApiKeyCommandHandler : IRequestHandler<CreateApiKeyCommand, ErrorOr<CreateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<CreateApiKeyCommandHandler> _logger;

    public CreateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IPermissionRepository permissionRepository,
        IPasswordHasher passwordHasher,
        ILogger<CreateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _permissionRepository = permissionRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<CreateApiKeyResponse>> Handle(
        CreateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        // Generate the API key using Argon2id
        var (apiKeyValue, keyPrefix, keyHash) = GenerateApiKey(request.Environment, _passwordHasher);

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

        return new CreateApiKeyResponse
        {
            Id = apiKey.Id,
            ApiKey = apiKeyValue,  // Return the actual key - only time it's visible
            KeyPrefix = keyPrefix,
            CreatedAt = apiKey.CreatedAt,
            ExpiresAt = apiKey.ExpiresAt
        };
    }

    private static (string ApiKey, string Prefix, string Hash) GenerateApiKey(string environment, IPasswordHasher passwordHasher)
    {
        // Generate 32 random bytes
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var randomPart = Convert.ToBase64String(randomBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..32];

        // Create prefix based on environment
        var prefix = environment switch
        {
            "production" => "ak_prod_",
            "staging" => "ak_stag_",
            "development" => "ak_dev_",
            _ => "ak_"
        };

        var apiKey = $"{prefix}{randomPart}";

        // Hash the API key using Argon2id
        var hash = passwordHasher.HashPassword(apiKey);

        return (apiKey, prefix, hash);
    }
}
