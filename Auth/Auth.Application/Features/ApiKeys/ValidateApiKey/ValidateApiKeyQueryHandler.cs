using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.ValidateApiKey;

/// <summary>
/// Handler for validating an API key by verifying it against stored Argon2id hashes.
/// </summary>
public class ValidateApiKeyQueryHandler : IRequestHandler<ValidateApiKeyQuery, ErrorOr<ValidateApiKeyResponse>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ValidateApiKeyQueryHandler> _logger;

    public ValidateApiKeyQueryHandler(
        IApiKeyRepository apiKeyRepository,
        IPasswordHasher passwordHasher,
        ILogger<ValidateApiKeyQueryHandler> logger)
    {
        _apiKeyRepository = apiKeyRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ErrorOr<ValidateApiKeyResponse>> Handle(
        ValidateApiKeyQuery request,
        CancellationToken cancellationToken)
    {
        var prefix = ExtractPrefix(request.RawApiKey);
        if (prefix is null)
        {
            return ApiKeyErrors.Invalid;
        }

        var candidates = await _apiKeyRepository.GetActiveByPrefixAsync(prefix, cancellationToken);
        if (candidates.Count == 0)
        {
            return ApiKeyErrors.Invalid;
        }

        foreach (var candidate in candidates)
        {
            if (_passwordHasher.VerifyPassword(request.RawApiKey, candidate.KeyHash))
            {
                await _apiKeyRepository.RecordUsageAsync(candidate.Id, cancellationToken);

                var scopes = await _apiKeyRepository.GetScopesAsync(candidate.Id, cancellationToken);

                _logger.LogInformation(
                    "API key validated: {ApiKeyId} for application {ApplicationId}",
                    candidate.Id, candidate.ApplicationId);

                return new ValidateApiKeyResponse
                {
                    Active = true,
                    ApiKeyId = candidate.Id,
                    ApplicationId = candidate.ApplicationId,
                    Name = candidate.Name,
                    Environment = candidate.Environment,
                    Scopes = scopes,
                    RateLimitPerMinute = candidate.RateLimitPerMinute,
                    RateLimitPerDay = candidate.RateLimitPerDay
                };
            }
        }

        _logger.LogWarning("API key validation failed for prefix {Prefix}", prefix);
        return ApiKeyErrors.Invalid;
    }

    private static string? ExtractPrefix(string rawApiKey)
    {
        // API key format: ak_<env>_<random> (e.g., ak_prod_abc123...)
        // Prefix is everything up to and including the second underscore
        var firstUnderscore = rawApiKey.IndexOf('_');
        if (firstUnderscore < 0)
            return null;

        var secondUnderscore = rawApiKey.IndexOf('_', firstUnderscore + 1);
        if (secondUnderscore < 0)
        {
            // Fallback for keys like "ak_<random>" without environment
            return rawApiKey[..(firstUnderscore + 1)];
        }

        return rawApiKey[..(secondUnderscore + 1)];
    }
}
