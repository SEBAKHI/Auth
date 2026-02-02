using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.AuditLogManagement.Queries;

/// <summary>
/// Query to get an audit log entry by ID.
/// </summary>
public record GetAuditLogByIdQuery(Guid Id) : IRequest<ErrorOr<AuditLogDto>>;
