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

    public GetWebhookKeysQueryHandler(IWebhookKeyRepository webhookKeyRepository)
    {
        _webhookKeyRepository = webhookKeyRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<WebhookKeyDto>>> Handle(
        GetWebhookKeysQuery request,
        CancellationToken cancellationToken)
    {
        var webhookKeys = await _webhookKeyRepository.GetByApplicationAsync(
            request.ApplicationId, request.SortBy, request.SortDirection, cancellationToken);

        var dtos = webhookKeys.Select(wk => new WebhookKeyDto
        {
            Id = wk.Id,
            ApplicationId = wk.ApplicationId,
            Name = wk.Name,
            Description = wk.Description,
            KeyPrefix = wk.KeyPrefix,
            TargetUrl = wk.TargetUrl,
            Environment = wk.Environment,
            CreatedAt = wk.CreatedAt,
            ExpiresAt = wk.ExpiresAt,
            LastUsedAt = wk.LastUsedAt,
            IsRevoked = wk.IsRevoked,
            RevokedAt = wk.RevokedAt
        }).ToList();

        return dtos;
    }
}
