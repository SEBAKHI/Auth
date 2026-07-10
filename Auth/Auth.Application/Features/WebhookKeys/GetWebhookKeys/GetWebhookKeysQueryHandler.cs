using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.WebhookKeys.GetWebhookKeys;

/// <summary>
/// Handler for getting webhook keys.
/// </summary>
public class GetWebhookKeysQueryHandler : IRequestHandler<GetWebhookKeysQuery, ErrorOr<IReadOnlyList<WebhookKeyDto>>>
{
    private readonly IWebhookKeyRepository _webhookKeyRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IUserRepository _userRepository;

    public GetWebhookKeysQueryHandler(
        IWebhookKeyRepository webhookKeyRepository,
        IApplicationRepository applicationRepository,
        IUserRepository userRepository)
    {
        _webhookKeyRepository = webhookKeyRepository;
        _applicationRepository = applicationRepository;
        _userRepository = userRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<WebhookKeyDto>>> Handle(
        GetWebhookKeysQuery request,
        CancellationToken cancellationToken)
    {
        var webhookKeys = await _webhookKeyRepository.GetByApplicationAsync(
            request.ApplicationId, request.SortBy, request.SortDirection, cancellationToken);

        var applicationNames = await NameLookupHelper.ApplicationNamesAsync(
            _applicationRepository,
            webhookKeys.Select(wk => (Guid?)wk.ApplicationId),
            cancellationToken);

        var userNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            webhookKeys.Select(wk => (Guid?)wk.CreatedBy),
            cancellationToken);

        var dtos = webhookKeys.Select(wk => new WebhookKeyDto
        {
            Id = wk.Id,
            ApplicationId = wk.ApplicationId,
            ApplicationName = applicationNames.GetValueOrDefault(wk.ApplicationId),
            Name = wk.Name,
            Description = wk.Description,
            KeyPrefix = wk.KeyPrefix,
            TargetUrl = wk.TargetUrl,
            Environment = wk.Environment,
            CreatedAt = wk.CreatedAt,
            CreatedBy = wk.CreatedBy,
            CreatedByName = userNames.GetValueOrDefault(wk.CreatedBy),
            ExpiresAt = wk.ExpiresAt,
            LastUsedAt = wk.LastUsedAt,
            IsRevoked = wk.IsRevoked,
            RevokedAt = wk.RevokedAt
        }).ToList();

        return dtos;
    }
}
