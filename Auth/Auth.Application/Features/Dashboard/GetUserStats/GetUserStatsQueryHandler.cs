using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetUserStats;

/// <summary>
/// Handler computing dashboard user statistics from database aggregates.
/// </summary>
public class GetUserStatsQueryHandler : IRequestHandler<GetUserStatsQuery, ErrorOr<UserStatsDto>>
{
    private readonly IDashboardStatsRepository _dashboardStatsRepository;
    private readonly ILogger<GetUserStatsQueryHandler> _logger;

    public GetUserStatsQueryHandler(
        IDashboardStatsRepository dashboardStatsRepository,
        ILogger<GetUserStatsQueryHandler> logger)
    {
        _dashboardStatsRepository = dashboardStatsRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<UserStatsDto>> Handle(GetUserStatsQuery request, CancellationToken cancellationToken)
    {
        var timeZone = request.TimeZone ?? "UTC";
        var snapshot = await _dashboardStatsRepository.GetUserStatsAsync(
            request.Days,
            timeZone,
            cancellationToken);

        _logger.LogDebug(
            "Computed user stats over {Days} days in {TimeZone} ({TotalUsers} users)",
            request.Days,
            timeZone,
            snapshot.TotalUsers);

        return new UserStatsDto
        {
            Days = request.Days,
            TotalUsers = snapshot.TotalUsers,
            ByStatus = snapshot.ByStatus
                .Select(s => new UserStatusCountDto { Status = s.Status, Count = s.Count })
                .ToList(),
            ActiveUsers = snapshot.ActiveUsers,
            MfaEnabled = snapshot.MfaEnabled,
            NewInWindow = snapshot.NewInWindow,
            SignupsPerDay = snapshot.SignupsPerDay
                .Select(d => new DailyCountDto { Date = d.Date, Count = d.Count })
                .ToList(),
            CohortCreated = snapshot.CohortCreated,
            CohortEmailConfirmed = snapshot.CohortEmailConfirmed,
            CohortLoggedIn = snapshot.CohortLoggedIn,
            DormantOver30Days = snapshot.DormantOver30Days,
            DormantOver60Days = snapshot.DormantOver60Days,
            DormantOver90Days = snapshot.DormantOver90Days,
            NeverLoggedIn = snapshot.NeverLoggedIn,
            UsersByOrganization = snapshot.UsersByOrganization
                .Select(o => new OrganizationUserCountDto
                {
                    OrganizationId = o.OrganizationId,
                    OrganizationName = o.OrganizationName,
                    IsAutoCreated = o.IsAutoCreated,
                    Count = o.Count
                })
                .ToList(),
            TotalActiveMemberships = snapshot.TotalActiveMemberships
        };
    }
}
