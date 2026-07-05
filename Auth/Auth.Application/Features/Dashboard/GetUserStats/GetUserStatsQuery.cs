using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetUserStats;

/// <summary>
/// Query to get aggregated dashboard user statistics over a trailing window of days.
/// </summary>
public record GetUserStatsQuery(int Days = 30) : IRequest<ErrorOr<UserStatsDto>>;
