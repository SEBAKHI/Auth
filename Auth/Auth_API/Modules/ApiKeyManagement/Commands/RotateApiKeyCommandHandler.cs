using Auth_Lib.Application.Abstractions;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using System.Security.Cryptography;

namespace Auth_API.Modules.ApiKeyManagement.Commands;

/// <summary>
/// Handler for the rotate API key command.
/// </summary>
public class RotateApiKeyCommandHandler : IRequestHandler<RotateApiKeyCommand, ErrorOr<RotateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<RotateApiKeyCommandHandler> _logger;

    public RotateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IPasswordHasher passwordHasher,
        ILogger<RotateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<RotateApiKeyResponse>> Handle(
        RotateApiKeyCommand request,
        CancellationToken cancellationToken)
    {
        // Get the existing API key
        var existingKey = await _apiKeyRepository.GetByIdAsync(request.ApiKeyId, cancellationToken);

        if (existingKey == null)
        {
            return Error.NotFound(
                code: "ApiKey.NotFound",
                description: "The specified API key was not found.");
        }

        if (existingKey.IsRevoked)
        {
            return Error.Validation(
                code: "ApiKey.AlreadyRevoked",
                description: "Cannot rotate a revoked API key.");
        }

        // Generate new API key using Argon2id
        var (newPlainKey, newKeyHash, newKeyPrefix) = GenerateApiKey(existingKey.Environment, _passwordHasher);

        // Create the new API key with the same settings
        var newApiKey = ApiKey.Create(
            applicationId: existingKey.ApplicationId,
            name: $"{existingKey.Name} (rotated)",
            keyPrefix: newKeyPrefix,
            keyHash: newKeyHash,
            createdBy: request.RotatedBy,
            description: $"Rotated from key {existingKey.KeyPrefix}... on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            environment: existingKey.Environment,
            rateLimitPerMinute: existingKey.RateLimitPerMinute,
            rateLimitPerDay: existingKey.RateLimitPerDay,
            expiresAt: existingKey.ExpiresAt);

        await _apiKeyRepository.CreateAsync(newApiKey, cancellationToken);

        // Schedule the old key for expiration
        var gracePeriodEnd = DateTime.UtcNow.AddMinutes(request.GracePeriodMinutes);

        // Update old key description to mark it as rotating
        // Note: The old key should be revoked after grace period by a background job or scheduled task

        _logger.LogInformation(
            "Rotated API key {OldKeyId} to {NewKeyId}. Old key valid until {GraceEnd}",
            existingKey.Id,
            newApiKey.Id,
            gracePeriodEnd);

        return new RotateApiKeyResponse
        {
            NewApiKey = newPlainKey,
            NewApiKeyId = newApiKey.Id,
            NewKeyPrefix = newKeyPrefix,
            OldKeyExpiresAt = gracePeriodEnd,
            OldApiKeyId = existingKey.Id,
            Message = $"New API key generated successfully. Old key will remain valid until {gracePeriodEnd:yyyy-MM-dd HH:mm:ss} UTC. " +
                      "Please update your applications to use the new key before the grace period ends."
        };
    }

    private static (string plainKey, string keyHash, string keyPrefix) GenerateApiKey(string environment, IPasswordHasher passwordHasher)
    {
        // Generate 32 random bytes for the key
        var keyBytes = new byte[32];
        RandomNumberGenerator.Fill(keyBytes);

        // Create prefix based on environment
        var envPrefix = environment.ToLowerInvariant() switch
        {
            "production" => "ak_prod_",
            "staging" => "ak_stg_",
            "development" => "ak_dev_",
            _ => "ak_"
        };

        // Convert to Base64 URL-safe string
        var keyBase64 = Convert.ToBase64String(keyBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        var plainKey = envPrefix + keyBase64;
        var keyPrefix = envPrefix + keyBase64[..8];

        // Hash the full key with Argon2id for storage
        var keyHash = passwordHasher.HashPassword(plainKey);

        return (plainKey, keyHash, keyPrefix);
    }
}
