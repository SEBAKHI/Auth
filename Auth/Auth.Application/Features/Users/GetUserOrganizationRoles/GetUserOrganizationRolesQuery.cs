using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserOrganizationRoles;

/// <summary>
/// The application roles a user holds through organizations, for the user page: shown
/// there read-only, each naming the organization where it is managed.
/// </summary>
public record GetUserOrganizationRolesQuery(Guid UserId) : IRequest<ErrorOr<IReadOnlyList<UserOrganizationRoleDto>>>;
