using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetAuthStats;

/// <summary>
/// Handler computing dashboard authentication statistics from database aggregates.
/// </summary>
public class GetAuthStatsQueryHandler : IRequestHandler<GetAuthStatsQuery, ErrorOr<AuthStatsDto>>
{
    private readonly IDashboardStatsRepository _dashboardStatsRepository;
    private readonly ILogger<GetAuthStatsQueryHandler> _logger;

    public GetAuthStatsQueryHandler(
        IDashboardStatsRepository dashboardStatsRepository,
        ILogger<GetAuthStatsQueryHandler> logger)
    {
        _dashboardStatsRepository = dashboardStatsRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<AuthStatsDto>> Handle(GetAuthStatsQuery request, CancellationToken cancellationToken)
    {
        var timeZone = request.TimeZone ?? "UTC";
        var snapshot = await _dashboardStatsRepository.GetAuthStatsAsync(
            request.Days,
            timeZone,
            cancellationToken);

        _logger.LogDebug(
            "Computed auth stats over {Days} days in {TimeZone} ({Success} successful / {Failed} failed attempts)",
            request.Days,
            timeZone,
            snapshot.WindowSuccessCount,
            snapshot.WindowFailureCount);

        return new AuthStatsDto
        {
            Days = request.Days,
            LoginsPerDay = snapshot.LoginsPerDay
                .Select(d => new DailyLoginCountDto
                {
                    Date = d.Date,
                    SuccessCount = d.SuccessCount,
                    FailureCount = d.FailureCount
                })
                .ToList(),
            ActiveUsersPerDay = snapshot.ActiveUsersPerDay
                .Select(d => new DailyCountDto { Date = d.Date, Count = d.Count })
                .ToList(),
            ActiveUsersInWindow = snapshot.ActiveUsersInWindow,
            FailureReasons = snapshot.FailureReasons
                .Select(r => new ReasonCountDto { Reason = r.Reason, Count = r.Count })
                .ToList(),
            WindowSuccessCount = snapshot.WindowSuccessCount,
            WindowFailureCount = snapshot.WindowFailureCount,
            PreviousWindowSuccessCount = snapshot.PreviousWindowSuccessCount,
            PreviousWindowFailureCount = snapshot.PreviousWindowFailureCount,
            LockedOutNow = snapshot.LockedOutNow,
            LockoutEventsInWindow = snapshot.LockoutEventsInWindow,
            TopFailingIps = snapshot.TopFailingIps
                .Select(ip => new IpFailureCountDto
                {
                    IpAddress = ip.IpAddress,
                    FailureCount = ip.FailureCount,
                    DistinctUsernames = ip.DistinctUsernames
                })
                .ToList(),
            LoginsByApplication = snapshot.LoginsByApplication
                .Select(a => new ApplicationLoginCountDto
                {
                    ApplicationId = a.ApplicationId,
                    ApplicationName = a.ApplicationName,
                    SuccessCount = a.SuccessCount,
                    FailureCount = a.FailureCount
                })
                .ToList(),
            LoginsByOrganization = snapshot.LoginsByOrganization
                .Select(o => new OrganizationLoginCountDto
                {
                    OrganizationId = o.OrganizationId,
                    OrganizationName = o.OrganizationName,
                    SuccessCount = o.SuccessCount,
                    FailureCount = o.FailureCount
                })
                .ToList()
        };
    }
}
