using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetSessionStats;

/// <summary>
/// Query to get aggregated dashboard session and refresh-token statistics over a trailing window of days.
/// </summary>
public record GetSessionStatsQuery(int Days = 30) : IRequest<ErrorOr<SessionStatsDto>>;
