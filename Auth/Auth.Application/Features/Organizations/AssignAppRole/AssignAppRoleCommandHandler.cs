using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.AssignAppRole;

/// <summary>
/// Handler for assigning an app-level role to a user within an organization.
/// </summary>
public class AssignAppRoleCommandHandler : IRequestHandler<AssignAppRoleCommand, ErrorOr<OrganizationMemberAppRoleDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRepository _userRepository;
    private readonly OrganizationGrantGuard _grantGuard;
    private readonly ILogger<AssignAppRoleCommandHandler> _logger;

    public AssignAppRoleCommandHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IUserRepository userRepository,
        OrganizationGrantGuard grantGuard,
        ILogger<AssignAppRoleCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _userRepository = userRepository;
        _grantGuard = grantGuard;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationMemberAppRoleDto>> Handle(
        AssignAppRoleCommand request,
        CancellationToken cancellationToken)
    {
        // Check organization exists
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Check user is a member
        var membership = await _organizationRepository.GetMembershipAsync(
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        if (membership == null)
        {
            return OrganizationErrors.NotMember(request.UserId, request.OrganizationId);
        }

        // Check application exists and is enabled for org
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application == null)
        {
            return OrganizationErrors.ApplicationNotFound(request.ApplicationId);
        }

        var isAppEnabled = await _organizationRepository.IsApplicationEnabledAsync(
            request.OrganizationId,
            request.ApplicationId,
            cancellationToken);

        if (!isAppEnabled)
        {
            return OrganizationErrors.ApplicationNotEnabled(request.ApplicationId);
        }

        // Check role exists and belongs to the application
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return OrganizationErrors.RoleNotFound(request.RoleId);
        }

        if (role.ApplicationId != request.ApplicationId)
        {
            return OrganizationErrors.RoleNotForApplication(request.RoleId, request.ApplicationId);
        }

        // No amplification. Assigning a role hands over every permission that
        // role carries, so it is a grant by another name — the same reasoning
        // the platform AssignRole path applies, and the reason checking only
        // "who may assign" leaves the more important question unasked.
        // Checked against the role's CURRENT permissions: a role that gains one
        // later is re-evaluated on the next assignment.
        var rolePermissions = await _permissionRepository.GetRolePermissionsAsync(
            request.RoleId, cancellationToken);

        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            request.OrganizationId,
            request.AssignedBy,
            request.ApplicationId,
            rolePermissions.Select(permission => permission.Code.Value),
            cancellationToken);
        if (canGrant.IsError)
        {
            _logger.LogWarning(
                "Blocked assignment of app role {RoleId} ({RoleName}) to user {UserId} in org {OrganizationId}: actor {AssignedBy} does not hold every permission the role carries",
                request.RoleId, role.Name, request.UserId, request.OrganizationId, request.AssignedBy);
            return canGrant.Errors;
        }

        // Check if already assigned
        if (await _organizationRepository.HasAppRoleAsync(
            request.OrganizationId,
            request.UserId,
            request.ApplicationId,
            request.RoleId,
            cancellationToken))
        {
            return OrganizationErrors.AppRoleAlreadyAssigned(request.UserId, request.ApplicationId, request.RoleId);
        }

        // Create role assignment
        var assignment = OrganizationUserRole.Create(
            request.OrganizationId,
            request.UserId,
            request.ApplicationId,
            request.RoleId,
            request.AssignedBy,
            request.ExpiresAt);

        await _organizationRepository.AssignAppRoleAsync(assignment, cancellationToken);

        // Get assigner info
        var assignedByUser = await _userRepository.GetByIdAsync(request.AssignedBy, cancellationToken);

        _logger.LogInformation(
            "Role {RoleId} assigned to user {UserId} for app {ApplicationId} in org {OrganizationId} by {AssignedBy}",
            request.RoleId, request.UserId, request.ApplicationId, request.OrganizationId, request.AssignedBy);

        return new OrganizationMemberAppRoleDto
        {
            Id = assignment.Id,
            ApplicationId = assignment.ApplicationId,
            ApplicationCode = application.Code,
            ApplicationName = application.Name,
            RoleId = assignment.RoleId,
            RoleCode = role.Code,
            RoleName = role.Name,
            AssignedAt = assignment.AssignedAt,
            AssignedBy = assignment.AssignedBy,
            AssignedByName = assignedByUser != null ? $"{assignedByUser.FirstName} {assignedByUser.LastName}".Trim() : null,
            ExpiresAt = assignment.ExpiresAt
        };
    }
}
