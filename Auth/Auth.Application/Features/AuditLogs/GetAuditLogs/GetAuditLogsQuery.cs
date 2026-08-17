using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>
/// Query to get a paginated list of audit logs with optional filtering.
/// </summary>
/// <remarks>
/// This used to carry <c>ActionType</c> and <c>IsSuccess</c> as well. Both were
/// accepted here, forwarded through the handler and bound as SQL parameters —
/// and never referenced by the WHERE clause. The AuditLogs table has no such
/// columns to filter on: the row mapper hardcodes "System" and true for every
/// entry, and the write path never persisted either. A filtered request quietly
/// returned the unfiltered page, which is worse than a rejected one.
/// </remarks>
public record GetAuditLogsQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<PagedAuditLogsDto>>;
