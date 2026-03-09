using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Handler for getting API keys.
/// </summary>
public class GetApiKeysQueryHandler : IRequestHandler<GetApiKeysQuery, ErrorOr<IReadOnlyList<ApiKeyDto>>>
{
    private readonly IApiKeyRepository _apiKeyRepository;

    public GetApiKeysQueryHandler(IApiKeyRepository apiKeyRepository)
    {
        _apiKeyRepository = apiKeyRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<ApiKeyDto>>> Handle(
        GetApiKeysQuery request,
        CancellationToken cancellationToken)
    {
        var apiKeys = await _apiKeyRepository.GetByApplicationAsync(request.ApplicationId, cancellationToken);

        var apiKeyDtos = new List<ApiKeyDto>();
        foreach (var apiKey in apiKeys)
        {
            var scopes = await _apiKeyRepository.GetScopesAsync(apiKey.Id, cancellationToken);
            apiKeyDtos.Add(new ApiKeyDto
            {
                Id = apiKey.Id,
                ApplicationId = apiKey.ApplicationId,
                Name = apiKey.Name,
                Description = apiKey.Description,
                KeyPrefix = apiKey.KeyPrefix,
                Environment = apiKey.Environment,
                RateLimitPerMinute = apiKey.RateLimitPerMinute,
                RateLimitPerDay = apiKey.RateLimitPerDay,
                CreatedAt = apiKey.CreatedAt,
                ExpiresAt = apiKey.ExpiresAt,
                LastUsedAt = apiKey.LastUsedAt,
                IsRevoked = apiKey.IsRevoked,
                RevokedAt = apiKey.RevokedAt,
                Scopes = scopes
            });
        }

        return apiKeyDtos;
    }
}
