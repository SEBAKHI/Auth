using Auth.Domain.Entities;
using ApplicationEntity = Auth.Domain.Entities.Application;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;

namespace Auth.Application.Common;

/// <summary>
/// The one answer to "may this actor give this application role to a member of this
/// organization": the application exists and the organization has it enabled, the role
/// belongs to that application, and the actor holds every permission the role carries
/// (<see cref="OrganizationGrantGuard"/>).
/// </summary>
/// <remarks>
/// Three writes ask it: assigning an application role to a member, naming one in an
/// invitation, and assigning that one when the invitation is accepted. One definition,
/// so an invitation can never carry a role the direct assignment would have refused,
/// and the role picker (which applies the guard's <see cref="OrganizationGrantGuard.Covers"/>
/// to the same reads) offers exactly what these accept.
/// </remarks>
public class OrganizationApplicationRoleCheck
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly OrganizationGrantGuard _grantGuard;

    public OrganizationApplicationRoleCheck(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        OrganizationGrantGuard grantGuard)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _grantGuard = grantGuard;
    }

    /// <summary>
    /// Confirms the actor may give <paramref name="roleId"/> of
    /// <paramref name="applicationId"/> inside <paramref name="organizationId"/>, and
    /// returns the application and the role for the caller's response.
    /// </summary>
    /// <param name="platformAuthorityInToken">
    /// Whether the actor's platform permissions count: the token's word, passed through
    /// to <see cref="OrganizationGrantGuard.EnsureCanGrantAsync"/>.
    /// </param>
    public async Task<ErrorOr<(ApplicationEntity Application, Role Role)>> EnsureAssignableAsync(
        Guid organizationId,
        Guid actorId,
        Guid applicationId,
        Guid roleId,
        bool platformAuthorityInToken,
        CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(applicationId, cancellationToken);
        if (application is null)
        {
            return OrganizationErrors.ApplicationNotFound(applicationId);
        }

        if (!await _organizationRepository.IsApplicationEnabledAsync(organizationId, applicationId, cancellationToken))
        {
            return OrganizationErrors.ApplicationNotEnabled(applicationId);
        }

        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken);
        if (role is null)
        {
            return OrganizationErrors.RoleNotFound(roleId);
        }

        if (role.ApplicationId != applicationId)
        {
            return OrganizationErrors.RoleNotForApplication(roleId, applicationId);
        }

        // No amplification. Assigning a role hands over every permission that role
        // carries, so it is a grant by another name. Checked against the role's
        // CURRENT permissions: a role that gains one later is re-evaluated on the next
        // assignment.
        var rolePermissions = await _permissionRepository.GetRolePermissionsAsync(roleId, cancellationToken);

        var canGrant = await _grantGuard.EnsureCanGrantAsync(
            organizationId,
            actorId,
            applicationId,
            rolePermissions.Select(permission => permission.Code.Value),
            platformAuthorityInToken,
            cancellationToken);
        if (canGrant.IsError)
        {
            return canGrant.Errors;
        }

        return (application, role);
    }
}
