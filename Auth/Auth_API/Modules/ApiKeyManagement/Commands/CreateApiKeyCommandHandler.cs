using System.Security.Cryptography;
using System.Text;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApiKeyManagement.Commands;

/// <summary>
/// Handler for creating a new API key.
/// </summary>
public class CreateApiKeyCommandHandler : IRequestHandler<CreateApiKeyCommand, ErrorOr<CreateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<CreateApiKeyCommandHandler> _logger;

    public CreateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IPermissionRepository permissionRepository,
        ILogger<CreateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<CreateApiKeyResponse>> Handle(
        CreateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        // Generate the API key
        var (apiKeyValue, keyPrefix, keyHash) = GenerateApiKey(request.Environment);

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

    private static (string ApiKey, string Prefix, string Hash) GenerateApiKey(string environment)
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
        var hash = ComputeSha256Hash(apiKey);

        return (apiKey, prefix, hash);
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
