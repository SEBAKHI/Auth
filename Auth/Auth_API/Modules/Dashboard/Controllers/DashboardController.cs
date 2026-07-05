using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Dashboard.GetAppActivityStats;
using Auth.Application.Features.Dashboard.GetAuthStats;
using Auth.Application.Features.Dashboard.GetSessionStats;
using Auth.Application.Features.Dashboard.GetUserStats;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Dashboard.Controllers;

/// <summary>
/// Controller exposing server-side dashboard aggregates. Every metric is computed
/// in the database over the full tables; day buckets are UTC calendar days.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
public class DashboardController : ApiController
{
    private readonly ISender _sender;

    public DashboardController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get user totals, status mix, signups, activation funnel, dormancy and
    /// per-organization membership over the trailing window.
    /// </summary>
    [HttpGet("user-stats")]
    [RequirePermission("users:read")]
    [ProducesResponseType(typeof(UserStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserStats(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetUserStatsQuery(days), cancellationToken);

        return result.Match(
            stats => Ok(stats),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get login attempt outcomes, daily active users, failure reasons, lockouts,
    /// top failing IPs and per-application/per-organization splits over the trailing window.
    /// </summary>
    [HttpGet("auth-stats")]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(AuthStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuthStats(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAuthStatsQuery(days), cancellationToken);

        return result.Match(
            stats => Ok(stats),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get session and refresh-token hygiene aggregates over the trailing window.
    /// </summary>
    [HttpGet("session-stats")]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(SessionStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSessionStats(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetSessionStatsQuery(days), cancellationToken);

        return result.Match(
            stats => Ok(stats),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get per-application activity and organization/application enablements over the trailing window.
    /// </summary>
    [HttpGet("app-activity")]
    [RequirePermission("applications:read")]
    [ProducesResponseType(typeof(AppActivityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAppActivity(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAppActivityStatsQuery(days), cancellationToken);

        return result.Match(
            stats => Ok(stats),
            errors => Problem(errors));
    }
}
