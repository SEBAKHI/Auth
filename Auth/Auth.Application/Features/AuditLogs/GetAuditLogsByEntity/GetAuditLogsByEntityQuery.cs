using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AuditLogs.GetAuditLogsByEntity;

/// <summary>
/// Query to get audit logs for a specific entity.
/// </summary>
public record GetAuditLogsByEntityQuery(
    string EntityType,
    Guid EntityId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<AuditLogDto>>>;
