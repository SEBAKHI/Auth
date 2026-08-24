using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>
/// Query to get a paginated list of audit logs with optional filtering.
/// </summary>
/// <remarks>
/// <c>ActionType</c> and <c>IsSuccess</c> were carried here once before, bound
/// as SQL parameters and never referenced by the WHERE clause, because the table
/// had no such columns — a filtered request quietly returned the unfiltered
/// page. They were removed rather than left as decoration. The columns exist
/// now, and both filters are applied.
///
/// <c>IsSuccess</c> matches on equality, so rows written before the column
/// existed are excluded from both true and false. Their outcome was never
/// recorded, and neither answer would be honest about them.
/// </remarks>
public record GetAuditLogsQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? Action = null,
    string? ActionType = null,
    bool? IsSuccess = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedAuditLogsDto>>;
