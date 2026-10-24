using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.GetUserOrganizationRoles;

/// <summary>
/// Handler for the application roles a user holds through organizations.
/// </summary>
/// <remarks>
/// A role held through an organization stays in that application's tokens when the
/// administrator removes the same role at user level, and the user page showed only
/// the user-level one. This read lets the page show the other source and where it is
/// managed. One read for the rows, whatever their number; nothing per row.
/// </remarks>
public class GetUserOrganizationRolesQueryHandler
    : IRequestHandler<GetUserOrganizationRolesQuery, ErrorOr<IReadOnlyList<UserOrganizationRoleDto>>>
{
    private readonly IUserRepository _userRepository;
    private readonly IOrganizationRepository _organizationRepository;

    public GetUserOrganizationRolesQueryHandler(
        IUserRepository userRepository,
        IOrganizationRepository organizationRepository)
    {
        _userRepository = userRepository;
        _organizationRepository = organizationRepository;
    }

    public async Task<ErrorOr<IReadOnlyList<UserOrganizationRoleDto>>> Handle(
        GetUserOrganizationRolesQuery request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        var rows = await _organizationRepository.GetUserApplicationRolesAcrossOrganizationsAsync(
            request.UserId, cancellationToken);

        return rows.Select(row => new UserOrganizationRoleDto
        {
            OrganizationId = row.OrganizationId,
            OrganizationCode = row.OrganizationCode,
            OrganizationName = row.OrganizationName,
            ApplicationId = row.ApplicationId,
            ApplicationCode = row.ApplicationCode,
            ApplicationName = row.ApplicationName,
            ApplicationIsDeleted = row.ApplicationIsDeleted,
            RoleId = row.RoleId,
            RoleCode = row.RoleCode,
            RoleName = row.RoleName,
            ExpiresAt = row.ExpiresAt
        }).ToList();
    }
}
