using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Roles.GetRoleApplications;

/// <summary>
/// Query to get the applications related to a role.
/// </summary>
public record GetRoleApplicationsQuery(
    Guid RoleId,
    string? SortBy = null,
    SortDirection SortDirection = SortDirection.Asc) : IRequest<ErrorOr<IReadOnlyList<RoleApplicationDto>>>;
