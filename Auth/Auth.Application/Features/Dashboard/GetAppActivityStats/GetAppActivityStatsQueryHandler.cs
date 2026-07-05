using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetAppActivityStats;

/// <summary>
/// Handler computing per-application activity and enablement statistics from database aggregates.
/// </summary>
public class GetAppActivityStatsQueryHandler : IRequestHandler<GetAppActivityStatsQuery, ErrorOr<AppActivityDto>>
{
    private readonly IDashboardStatsRepository _dashboardStatsRepository;
    private readonly ILogger<GetAppActivityStatsQueryHandler> _logger;

    public GetAppActivityStatsQueryHandler(
        IDashboardStatsRepository dashboardStatsRepository,
        ILogger<GetAppActivityStatsQueryHandler> logger)
    {
        _dashboardStatsRepository = dashboardStatsRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<AppActivityDto>> Handle(GetAppActivityStatsQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await _dashboardStatsRepository.GetAppActivityAsync(request.Days, cancellationToken);

        _logger.LogDebug(
            "Computed app activity over {Days} days ({Apps} applications)",
            request.Days, snapshot.Applications.Count);

        return new AppActivityDto
        {
            Days = request.Days,
            Applications = snapshot.Applications
                .Select(a => new ApplicationActivityDto
                {
                    ApplicationId = a.ApplicationId,
                    ApplicationName = a.ApplicationName,
                    IsActive = a.IsActive,
                    SuccessfulLogins = a.SuccessfulLogins,
                    DistinctUsers = a.DistinctUsers,
                    ActiveSessions = a.ActiveSessions
                })
                .ToList(),
            OrganizationApplications = snapshot.OrganizationApplications
                .Select(e => new OrganizationApplicationEnablementDto
                {
                    OrganizationId = e.OrganizationId,
                    OrganizationName = e.OrganizationName,
                    ApplicationId = e.ApplicationId,
                    ApplicationName = e.ApplicationName,
                    SubscriptionTier = e.SubscriptionTier,
                    ExpiresAt = e.ExpiresAt
                })
                .ToList()
        };
    }
}
