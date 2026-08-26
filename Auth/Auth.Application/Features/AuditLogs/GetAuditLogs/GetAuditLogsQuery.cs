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
///
/// <c>ParticipantId</c> replaced a bare <c>UserId</c>, which was applied to the
/// subject column alone — so "everything this operator did" was a question this
/// endpoint could not be asked, and the screen built on it answered a different
/// question under the same heading. <c>ParticipantRole</c> is required whenever
/// an id is present: a role that defaults to the widest reading would widen
/// every existing caller in silence, which is the same failure as a filter that
/// silently does nothing.
/// </remarks>
public record GetAuditLogsQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Guid? ParticipantId = null,
    AuditParticipantRole? ParticipantRole = null,
    Guid? ApplicationId = null,
    string? Action = null,
    string? ActionType = null,
    bool? IsSuccess = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedAuditLogsDto>>;
