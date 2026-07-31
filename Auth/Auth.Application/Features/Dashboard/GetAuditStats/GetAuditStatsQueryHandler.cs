using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetAuditStats;

/// <summary>
/// Handler computing dashboard audit-event statistics from database aggregates.
/// </summary>
public class GetAuditStatsQueryHandler : IRequestHandler<GetAuditStatsQuery, ErrorOr<AuditStatsDto>>
{
    private readonly IDashboardStatsRepository _dashboardStatsRepository;
    private readonly ILogger<GetAuditStatsQueryHandler> _logger;

    public GetAuditStatsQueryHandler(
        IDashboardStatsRepository dashboardStatsRepository,
        ILogger<GetAuditStatsQueryHandler> logger)
    {
        _dashboardStatsRepository = dashboardStatsRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<AuditStatsDto>> Handle(GetAuditStatsQuery request, CancellationToken cancellationToken)
    {
        var timeZone = request.TimeZone ?? "UTC";
        var snapshot = await _dashboardStatsRepository.GetAuditStatsAsync(
            request.Days,
            timeZone,
            cancellationToken);

        _logger.LogDebug(
            "Computed audit stats over {Days} days in {TimeZone} ({Total} events, {Previous} in the previous window)",
            request.Days,
            timeZone,
            snapshot.TotalInWindow,
            snapshot.PreviousWindowTotal);

        return new AuditStatsDto
        {
            Days = request.Days,
            TotalInWindow = snapshot.TotalInWindow,
            PreviousWindowTotal = snapshot.PreviousWindowTotal,
            EventsPerDay = snapshot.EventsPerDay
                .Select(d => new DailyCountDto { Date = d.Date, Count = d.Count })
                .ToList(),
            TopActions = snapshot.TopActions
                .Select(a => new ReasonCountDto { Reason = a.Reason, Count = a.Count })
                .ToList(),
            ByEntityType = snapshot.ByEntityType
                .Select(e => new ReasonCountDto { Reason = e.Reason, Count = e.Count })
                .ToList()
        };
    }
}
