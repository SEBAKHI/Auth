using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetAuthStats;

/// <summary>
/// Query to get aggregated dashboard authentication statistics over a trailing window of days.
/// </summary>
public record GetAuthStatsQuery(int Days = 30) : IRequest<ErrorOr<AuthStatsDto>>;
