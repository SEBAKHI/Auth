using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ReadModels.Dashboard;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetCredentialStats;

/// <summary>
/// Handler computing credential-expiry statistics from database aggregates, filtered
/// to the credential families the caller is allowed to read.
/// </summary>
public class GetCredentialStatsQueryHandler
    : IRequestHandler<GetCredentialStatsQuery, ErrorOr<CredentialStatsDto>>
{
    private const string ApiKeysRead = "apikeys:read";
    private const string WebhookKeysRead = "webhookkeys:read";

    private readonly IDashboardStatsRepository _dashboardStatsRepository;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ILogger<GetCredentialStatsQueryHandler> _logger;

    public GetCredentialStatsQueryHandler(
        IDashboardStatsRepository dashboardStatsRepository,
        IPermissionChecker permissionChecker,
        ILogger<GetCredentialStatsQueryHandler> logger)
    {
        _dashboardStatsRepository = dashboardStatsRepository;
        _permissionChecker = permissionChecker;
        _logger = logger;
    }

    public async Task<ErrorOr<CredentialStatsDto>> Handle(
        GetCredentialStatsQuery request,
        CancellationToken cancellationToken)
    {
        var canReadApiKeys = await _permissionChecker.HasPermissionAsync(
            request.RequestedBy, ApiKeysRead, null, cancellationToken);
        var canReadWebhookKeys = await _permissionChecker.HasPermissionAsync(
            request.RequestedBy, WebhookKeysRead, null, cancellationToken);

        // Neither family is visible: skip the database entirely rather than compute
        // four aggregates only to throw them away.
        if (!canReadApiKeys && !canReadWebhookKeys)
        {
            return new CredentialStatsDto { HorizonDays = request.HorizonDays };
        }

        var snapshot = await _dashboardStatsRepository.GetCredentialStatsAsync(
            request.HorizonDays, cancellationToken);

        _logger.LogDebug(
            "Computed credential expiry over a {HorizonDays}-day horizon ({ApiKeys} API keys, {WebhookKeys} webhook keys expiring)",
            snapshot.HorizonDays, snapshot.ApiKeys.ExpiringCount, snapshot.WebhookKeys.ExpiringCount);

        return new CredentialStatsDto
        {
            HorizonDays = snapshot.HorizonDays,
            ApiKeys = canReadApiKeys ? Map(snapshot.ApiKeys) : null,
            WebhookKeys = canReadWebhookKeys ? Map(snapshot.WebhookKeys) : null
        };
    }

    private static CredentialExpiryBucketDto Map(CredentialExpiryBucket bucket) => new()
    {
        ExpiringCount = bucket.ExpiringCount,
        SoonestExpiresAt = bucket.SoonestExpiresAt,
        TotalActive = bucket.TotalActive
    };
}
