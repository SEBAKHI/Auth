using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.RotateApiKey;

/// <summary>
/// Handler for the rotate API key command.
/// </summary>
public class RotateApiKeyCommandHandler : IRequestHandler<RotateApiKeyCommand, ErrorOr<RotateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApiKeyGenerator _apiKeyGenerator;
    private readonly ILogger<RotateApiKeyCommandHandler> _logger;

    public RotateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IApiKeyGenerator apiKeyGenerator,
        ILogger<RotateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _apiKeyGenerator = apiKeyGenerator;
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

        // Generate new API key
        var (newPlainKey, newKeyPrefix, newKeyHash) = _apiKeyGenerator.Generate(existingKey.Environment);

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

        // Schedule the old key to expire after the grace period
        var gracePeriodEnd = DateTime.UtcNow.AddMinutes(request.GracePeriodMinutes);
        existingKey.ScheduleExpiration(gracePeriodEnd);
        await _apiKeyRepository.UpdateAsync(existingKey, cancellationToken);

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
}
