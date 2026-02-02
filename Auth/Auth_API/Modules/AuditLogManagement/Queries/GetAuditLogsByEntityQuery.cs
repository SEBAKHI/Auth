using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.AuditLogManagement.Queries;

/// <summary>
/// Query to get audit logs for a specific entity.
/// </summary>
public record GetAuditLogsByEntityQuery(
    string EntityType,
    Guid EntityId) : IRequest<ErrorOr<IReadOnlyList<AuditLogDto>>>;
