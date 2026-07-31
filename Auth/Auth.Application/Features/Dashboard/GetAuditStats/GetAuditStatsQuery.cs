using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetAuditStats;

/// <summary>
/// Query to get aggregated dashboard audit-event statistics over a trailing window of days.
/// </summary>
public record GetAuditStatsQuery(
    int Days = 30,
    string? TimeZone = "UTC") : IRequest<ErrorOr<AuditStatsDto>>;
