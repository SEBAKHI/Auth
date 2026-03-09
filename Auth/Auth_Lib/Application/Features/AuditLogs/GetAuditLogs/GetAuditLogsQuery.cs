using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.AuditLogs.GetAuditLogs;

/// <summary>
/// Query to get a paginated list of audit logs with optional filtering.
/// </summary>
public record GetAuditLogsQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Guid? UserId = null,
    Guid? ApplicationId = null,
    string? ActionType = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool? IsSuccess = null) : IRequest<ErrorOr<PagedAuditLogsDto>>;
