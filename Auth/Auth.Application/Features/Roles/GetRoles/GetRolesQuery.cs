using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoles;

/// <summary>
/// Query to get roles for an application.
/// </summary>
public record GetRolesQuery(
    Guid? ApplicationId = null,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
