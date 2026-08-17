using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Common;
using Auth_API.Modules.AuditLogManagement.Contracts;
using Auth.Application.Features.AuditLogs.ExportAuditLogs;
using Auth.Application.Features.AuditLogs.GetAuditLogById;
using Auth.Application.Features.AuditLogs.GetAuditLogs;
using Auth.Application.Features.AuditLogs.GetAuditLogsByEntity;
using Auth.Application.Features.AuditLogs.GetAuditLogsByUser;
using Auth.Application.DTOs;
using Auth.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.AuditLogManagement.Controllers;

/// <summary>
/// Controller for audit log query and export operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audit-logs")]
[Authorize]
public class AuditLogsController : ApiController
{
    private readonly ISender _sender;

    public AuditLogsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Get a paginated list of audit logs with optional filtering.
    /// </summary>
    [HttpGet]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(PagedAuditLogsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? userId = null,
        [FromQuery] Guid? applicationId = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        // `actionType` and `isSuccess` used to be documented here and are gone:
        // the AuditLogs table has no such columns, the repository dropped both
        // before building its WHERE clause, and every row reports "System" and
        // true regardless. Removing them costs no consumer anything — an unknown
        // query parameter is ignored by model binding, so a caller still sending
        // them gets the same unfiltered page it has always received, minus the
        // 400 that a malformed `isSuccess` used to produce.
        var query = new GetAuditLogsQuery(
            pageNumber, pageSize, userId, applicationId,
            action, fromDate, toDate, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            logs => Ok(logs),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get an audit log entry by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(AuditLogDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLog(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetAuditLogByIdQuery(id);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            log => Ok(log),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get audit logs for a specific user.
    /// </summary>
    [HttpGet("users/{userId:guid}")]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(PagedAuditLogsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLogsByUser(
        Guid userId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAuditLogsByUserQuery(
            userId, pageNumber, pageSize, fromDate, toDate, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            logs => Ok(logs),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get audit logs for a specific entity.
    /// </summary>
    [HttpGet("entities/{entityType}/{entityId:guid}")]
    [RequirePermission("auditlogs:read")]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditLogsByEntity(
        string entityType,
        Guid entityId,
        [FromQuery] string? sortBy = null,
        [FromQuery] SortDirection sortDirection = SortDirection.Asc,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAuditLogsByEntityQuery(entityType, entityId, sortBy, sortDirection);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            logs => Ok(logs),
            errors => Problem(errors));
    }

    /// <summary>
    /// Export audit logs to a file (CSV or JSON).
    /// </summary>
    [HttpPost("export")]
    [RequirePermission("auditlogs:export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportAuditLogs([FromBody] ExportAuditLogsRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new ExportAuditLogsCommand(
            request.Format,
            request.UserId,
            request.ApplicationId,
            request.Action,
            request.FromDate,
            request.ToDate,
            request.MaxRecords,
            request.SortBy,
            request.SortDirection)
        {
            RequestedBy = userId
        };

        var result = await _sender.Send(command, cancellationToken);

        return result.Match<IActionResult>(
            export => File(export.Content, export.ContentType, export.FileName),
            errors => Problem(errors));
    }

}
