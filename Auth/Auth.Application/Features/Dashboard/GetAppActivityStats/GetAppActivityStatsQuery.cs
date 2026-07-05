using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetAppActivityStats;

/// <summary>
/// Query to get per-application activity and organization/application enablement over a trailing window of days.
/// </summary>
public record GetAppActivityStatsQuery(int Days = 30) : IRequest<ErrorOr<AppActivityDto>>;
