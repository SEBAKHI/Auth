using Auth.Application.Features.ApiKeys.Common;
using Auth.Domain.Errors;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.RotateApiKey;

/// <summary>
/// Handler for the rotate API key command.
/// </summary>
/// <remarks>
/// The new key carries the old key's scopes and addresses (OI-128 PR B), asked again as at
/// creation: a key with no scope cannot be rotated (create a new one instead), and the rotating
/// administrator must hold every scope it hands over. The new key, its scopes and the old key's
/// new end are written in one transaction.
/// </remarks>
public class RotateApiKeyCommandHandler : IRequestHandler<RotateApiKeyCommand, ErrorOr<RotateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApiKeyGenerator _apiKeyGenerator;
    private readonly ApiKeyScopePolicy _scopePolicy;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<RotateApiKeyCommandHandler> _logger;

    public RotateApiKeyCommandHandler(
        IApiKeyRepository apiKeyRepository,
        IApiKeyGenerator apiKeyGenerator,
        ApiKeyScopePolicy scopePolicy,
        IPublisher publisher,
        TimeProvider time,
        ILogger<RotateApiKeyCommandHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _apiKeyGenerator = apiKeyGenerator;
        _scopePolicy = scopePolicy;
        _publisher = publisher;
        _time = time;
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
            return ApiKeyErrors.NotFound;
        }

        if (existingKey.IsRevoked)
        {
            return ApiKeyErrors.AlreadyRevoked;
        }

        var permissionIds = await _apiKeyRepository.GetScopePermissionIdsAsync(existingKey.Id, cancellationToken);
        var scopes = await _scopePolicy.ResolveAsync(
            existingKey.ApplicationId, permissionIds, request.RotatedBy, cancellationToken);
        if (scopes.IsError)
        {
            _logger.LogWarning(
                "Blocked rotation of API key {ApiKeyId} by {RotatedBy}: {Code}",
                existingKey.Id, request.RotatedBy, scopes.FirstError.Code);
            return scopes.Errors;
        }

        var generated = _apiKeyGenerator.Generate(existingKey.Environment);
        var now = _time.GetUtcNow();

        var newApiKey = existingKey.CreateReplacement(
            generated.KeyPrefix, generated.KeySelector, generated.KeyHash, request.RotatedBy, now.UtcDateTime);

        // Schedule the old key to expire after the grace period
        var gracePeriodEnd = now.UtcDateTime.AddMinutes(request.GracePeriodMinutes);
        existingKey.ScheduleExpiration(gracePeriodEnd);

        var rotated = await _apiKeyRepository.TryRotateAsync(
            existingKey,
            newApiKey,
            scopes.Value.Select(permissionId => ApiKeyScope.Create(newApiKey.Id, permissionId, request.RotatedBy)).ToList(),
            cancellationToken);
        if (!rotated)
        {
            _logger.LogWarning("API key {ApiKeyId} was revoked during its rotation; nothing was issued", existingKey.Id);
            return ApiKeyErrors.AlreadyRevoked;
        }

        _logger.LogInformation(
            "Rotated API key {OldKeyId} to {NewKeyId}. Old key valid until {GraceEnd}",
            existingKey.Id,
            newApiKey.Id,
            gracePeriodEnd);

        await _publisher.Publish(
            new ApiKeyRotatedEvent(
                Guid.NewGuid(),
                newApiKey.Id,
                newApiKey.ApplicationId,
                existingKey.Id,
                gracePeriodEnd,
                request.RotatedBy,
                now),
            CancellationToken.None);

        return new RotateApiKeyResponse
        {
            NewApiKey = generated.PlainKey,
            NewApiKeyId = newApiKey.Id,
            NewKeyPrefix = generated.KeyPrefix,
            OldKeyExpiresAt = gracePeriodEnd,
            OldApiKeyId = existingKey.Id,
            Message = $"New API key generated successfully. Old key will remain valid until {gracePeriodEnd:yyyy-MM-dd HH:mm:ss} UTC. " +
                      "Please update your applications to use the new key before the grace period ends.",
            MessageCode = "ApiKey.Rotated"
        };
    }
}
