using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Dashboard.GetAppActivityStats;
using Auth.Application.Features.Dashboard.GetAuditStats;
using Auth.Application.Features.Dashboard.GetAuthStats;
using Auth.Application.Features.Dashboard.GetCredentialStats;
using Auth.Application.Features.Dashboard.GetSessionStats;
using Auth.Application.Features.Dashboard.GetUserStats;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Dashboard.Controllers;

/// <summary>
/// Controller exposing server-side dashboard aggregates. Every metric is computed
/// in the database over the full tables; day buckets use the validated viewer
/// time zone while stored instants remain UTC.
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
        [FromQuery] string timeZone = "UTC",
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetUserStatsQuery(days, timeZone), cancellationToken);

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
        [FromQuery] string timeZone = "UTC",
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAuthStatsQuery(days, timeZone), cancellationToken);

        return result.Match(
            stats => Ok(stats),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get audit-event totals, the daily series, and the action and entity-type
    /// breakdowns over the trailing window.
    /// </summary>
    /// <remarks>
    /// Server-side aggregate over the whole table. Reading a page of audit logs and
    /// bucketing it in the client cannot produce these numbers: a page is a sample.
    /// </remarks>
    [HttpGet("audit-stats")]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(AuditStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditStats(
        [FromQuery] int days = 30,
        [FromQuery] string timeZone = "UTC",
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetAuditStatsQuery(days, timeZone), cancellationToken);

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

    /// <summary>
    /// Get the expiry posture of issued API and webhook keys over a forward horizon.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT decorated with [RequirePermission]: the two credential families
    /// are gated by two different permissions (apikeys:read and webhookkeys:read) and the
    /// attribute takes only one. The handler resolves both and returns a null bucket for
    /// a family the caller may not read, so the dashboard can only ever surface a finding
    /// whose destination page that same caller can open.
    ///
    /// The horizon runs forward and is separate from the trailing window every other
    /// action on this controller takes.
    /// </remarks>
    [HttpGet("credential-stats")]
    [ProducesResponseType(typeof(CredentialStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCredentialStats(
        [FromQuery] int horizonDays = 14,
        CancellationToken cancellationToken = default)
    {
        var query = new GetCredentialStatsQuery(horizonDays) { RequestedBy = GetCurrentUserId() };
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            stats => Ok(stats),
            errors => Problem(errors));
    }
}
