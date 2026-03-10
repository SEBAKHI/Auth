using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogsByEntity;

/// <summary>
/// Query to get audit logs for a specific entity.
/// </summary>
public record GetAuditLogsByEntityQuery(
    string EntityType,
    Guid EntityId) : IRequest<ErrorOr<IReadOnlyList<AuditLogDto>>>;
