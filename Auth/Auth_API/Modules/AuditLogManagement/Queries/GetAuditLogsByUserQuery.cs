using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.AuditLogManagement.Queries;

/// <summary>
/// Query to get audit logs for a specific user.
/// </summary>
public record GetAuditLogsByUserQuery(
    Guid UserId,
    int PageNumber = 1,
    int PageSize = 50,
    DateTime? FromDate = null,
    DateTime? ToDate = null) : IRequest<ErrorOr<PagedAuditLogsDto>>;
