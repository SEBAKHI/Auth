using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Handler for getting API keys.
/// </summary>
public class GetApiKeysQueryHandler : IRequestHandler<GetApiKeysQuery, ErrorOr<IReadOnlyList<ApiKeyDto>>>
{
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;

    public GetApiKeysQueryHandler(
        IApiKeyRepository apiKeyRepository,
        IApplicationRepository applicationRepository,
        IUserRepository userRepository)
    {
        _apiKeyRepository = apiKeyRepository;
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<ApiKeyDto>>> Handle(
        GetApiKeysQuery request,
        CancellationToken cancellationToken)
    {
        var apiKeys = await _apiKeyRepository.GetByApplicationAsync(
            request.ApplicationId, request.SortBy, request.SortDirection, cancellationToken);

        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository,
            apiKeys.Select(key => (Guid?)key.ApplicationId),
            cancellationToken);

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            apiKeys.Select(key => (Guid?)key.CreatedBy),
            cancellationToken);

        var apiKeyDtos = new List<ApiKeyDto>();
        foreach (var apiKey in apiKeys)
        {
            var scopes = await _apiKeyRepository.GetScopesAsync(apiKey.Id, cancellationToken);
            apiKeyDtos.Add(new ApiKeyDto
            {
                Id = apiKey.Id,
                ApplicationId = apiKey.ApplicationId,
                ApplicationName = applicationNames.GetValueOrDefault(apiKey.ApplicationId),
                Name = apiKey.Name,
                Description = apiKey.Description,
                KeyPrefix = apiKey.KeyPrefix,
                Environment = apiKey.Environment,
                RateLimitPerMinute = apiKey.RateLimitPerMinute,
                RateLimitPerDay = apiKey.RateLimitPerDay,
                CreatedAt = apiKey.CreatedAt,
                CreatedBy = apiKey.CreatedBy,
                CreatedByName = userNames.GetValueOrDefault(apiKey.CreatedBy),
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
