using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.AssignAppRole;

/// <summary>
/// Handler for assigning an app-level role to a user within an organization.
/// </summary>
public class AssignAppRoleCommandHandler : IRequestHandler<AssignAppRoleCommand, ErrorOr<OrganizationMemberAppRoleDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly OrganizationApplicationRoleCheck _roleCheck;
    private readonly IPublisher _publisher;
    private readonly ILogger<AssignAppRoleCommandHandler> _logger;

    public AssignAppRoleCommandHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        OrganizationApplicationRoleCheck roleCheck,
        IPublisher publisher,
        ILogger<AssignAppRoleCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleCheck = roleCheck;
        _publisher = publisher;
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

        // The application, its enablement, the role's ownership and no amplification:
        // the same check an invitation naming an application role passes.
        var assignable = await _roleCheck.EnsureAssignableAsync(
            request.OrganizationId,
            request.AssignedBy,
            request.ApplicationId,
            request.RoleId,
            request.PlatformAuthorityInToken,
            cancellationToken);
        if (assignable.IsError)
        {
            _logger.LogWarning(
                "Blocked assignment of app role {RoleId} to user {UserId} in org {OrganizationId} by {AssignedBy}: {ErrorCode}",
                request.RoleId, request.UserId, request.OrganizationId, request.AssignedBy, assignable.FirstError.Code);
            return assignable.Errors;
        }

        var (application, role) = assignable.Value;

        // Refused only while a VALID assignment exists (active, not expired): an
        // expired or inactive one is re-assigned in place, on its own row.
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

        // Null when the membership was removed after the check above: the statement checks
        // it again under its own lock and writes nothing, so no role outlives a removal.
        var assignmentId = await _organizationRepository.AssignAppRoleAsync(assignment, cancellationToken);
        if (assignmentId is null)
        {
            return OrganizationErrors.NotMember(request.UserId, request.OrganizationId);
        }

        // Get assigner info
        var assignedByUser = await _userRepository.GetByIdAsync(request.AssignedBy, cancellationToken);

        _logger.LogInformation(
            "Role {RoleId} assigned to user {UserId} for app {ApplicationId} in org {OrganizationId} by {AssignedBy}",
            request.RoleId, request.UserId, request.ApplicationId, request.OrganizationId, request.AssignedBy);

        await _publisher.Publish(
            new OrganizationAppRoleAssignedEvent(
                request.OrganizationId,
                request.UserId,
                request.ApplicationId,
                request.RoleId,
                role.Name,
                assignment.ExpiresAt,
                request.AssignedBy),
            cancellationToken);

        return new OrganizationMemberAppRoleDto
        {
            Id = assignmentId.Value,
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
