using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogById;

/// <summary>
/// Query to get an audit log entry by ID.
/// </summary>
public record GetAuditLogByIdQuery(Guid Id) : IRequest<ErrorOr<AuditLogDto>>;
