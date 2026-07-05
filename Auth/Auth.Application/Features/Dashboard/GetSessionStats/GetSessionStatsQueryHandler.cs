using Auth.Application.DTOs;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetSessionStats;

/// <summary>
/// Handler computing dashboard session and refresh-token statistics from database aggregates.
/// </summary>
public class GetSessionStatsQueryHandler : IRequestHandler<GetSessionStatsQuery, ErrorOr<SessionStatsDto>>
{
    private readonly IDashboardStatsRepository _dashboardStatsRepository;
    private readonly ILogger<GetSessionStatsQueryHandler> _logger;

    public GetSessionStatsQueryHandler(
        IDashboardStatsRepository dashboardStatsRepository,
        ILogger<GetSessionStatsQueryHandler> logger)
    {
        _dashboardStatsRepository = dashboardStatsRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<SessionStatsDto>> Handle(GetSessionStatsQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await _dashboardStatsRepository.GetSessionStatsAsync(request.Days, cancellationToken);

        _logger.LogDebug(
            "Computed session stats over {Days} days ({Active} active sessions)",
            request.Days, snapshot.ActiveSessions);

        return new SessionStatsDto
        {
            Days = request.Days,
            ActiveSessions = snapshot.ActiveSessions,
            StaleOpenSessions = snapshot.StaleOpenSessions,
            StartedInWindow = snapshot.StartedInWindow,
            EndReasons = snapshot.EndReasons
                .Select(r => new ReasonCountDto { Reason = r.Reason, Count = r.Count })
                .ToList(),
            AverageSessionMinutes = snapshot.AverageSessionMinutes,
            ActiveRefreshTokens = snapshot.ActiveRefreshTokens,
            TokensRevokedInWindow = snapshot.TokensRevokedInWindow,
            RevocationReasons = snapshot.RevocationReasons
                .Select(r => new ReasonCountDto { Reason = r.Reason, Count = r.Count })
                .ToList(),
            TokensExpiringIn7Days = snapshot.TokensExpiringIn7Days
        };
    }
}
