using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetMemberAppRoles;

/// <summary>
/// Handler for getting all app-level role assignments for a member within an organization.
/// </summary>
public class GetMemberAppRolesQueryHandler : IRequestHandler<GetMemberAppRolesQuery, ErrorOr<IReadOnlyList<OrganizationMemberAppRoleDto>>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<GetMemberAppRolesQueryHandler> _logger;

    public GetMemberAppRolesQueryHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ILogger<GetMemberAppRolesQueryHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<IReadOnlyList<OrganizationMemberAppRoleDto>>> Handle(
        GetMemberAppRolesQuery request,
        CancellationToken cancellationToken)
    {
        // Check organization exists
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Check target user is a member
        var membership = await _organizationRepository.GetMembershipAsync(
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        if (membership == null)
        {
            return OrganizationErrors.NotMember(request.UserId, request.OrganizationId);
        }

        var assignments = await _organizationRepository.GetUserAppRolesAsync(
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        // Enrich with application, role, and assigner details
        var dtos = new List<OrganizationMemberAppRoleDto>();
        foreach (var assignment in assignments)
        {
            var application = await _applicationRepository.GetByIdAsync(assignment.ApplicationId, cancellationToken);
            var role = await _roleRepository.GetByIdAsync(assignment.RoleId, cancellationToken);
            var assignedByUser = await _userRepository.GetByIdAsync(assignment.AssignedBy, cancellationToken);

            dtos.Add(new OrganizationMemberAppRoleDto
            {
                Id = assignment.Id,
                ApplicationId = assignment.ApplicationId,
                ApplicationCode = application?.Code ?? string.Empty,
                ApplicationName = application?.Name ?? string.Empty,
                RoleId = assignment.RoleId,
                RoleCode = role?.Code ?? string.Empty,
                RoleName = role?.Name ?? string.Empty,
                AssignedAt = assignment.AssignedAt,
                AssignedBy = assignment.AssignedBy,
                AssignedByName = assignedByUser != null ? $"{assignedByUser.FirstName} {assignedByUser.LastName}".Trim() : null,
                ExpiresAt = assignment.ExpiresAt
            });
        }

        _logger.LogDebug(
            "Retrieved {Count} app role assignments for user {UserId} in organization {OrganizationId}",
            dtos.Count, request.UserId, request.OrganizationId);

        return dtos;
    }
}
