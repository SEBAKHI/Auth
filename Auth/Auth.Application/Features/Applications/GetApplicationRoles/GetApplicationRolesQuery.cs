using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.GetApplicationRoles;

/// <summary>
/// Query to get all roles for an application.
/// </summary>
public record GetApplicationRolesQuery(
    Guid ApplicationId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<RoleDto>>>;
