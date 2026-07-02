using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Permissions.GetPermissionImplications;

/// <summary>
/// Query to get all permissions implied by a permission.
/// </summary>
public record GetPermissionImplicationsQuery(
    Guid PermissionId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<PermissionDto>>>;
