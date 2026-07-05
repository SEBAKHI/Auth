using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.RemoveAppRole;

/// <summary>
/// Handler for removing an app-level role from a user within an organization.
/// </summary>
public class RemoveAppRoleCommandHandler : IRequestHandler<RemoveAppRoleCommand, ErrorOr<Deleted>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<RemoveAppRoleCommandHandler> _logger;

    public RemoveAppRoleCommandHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        ILogger<RemoveAppRoleCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<Deleted>> Handle(
        RemoveAppRoleCommand request,
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

        // Check role exists; the role determines the application
        var role = await _roleRepository.GetByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
        {
            return OrganizationErrors.RoleNotFound(request.RoleId);
        }

        // Platform roles (no application) can never be org app-role assignments
        if (role.ApplicationId is not Guid applicationId)
        {
            return OrganizationErrors.AppRoleNotAssigned(request.UserId, Guid.Empty, request.RoleId);
        }

        // Check the assignment exists
        if (!await _organizationRepository.HasAppRoleAsync(
            request.OrganizationId,
            request.UserId,
            applicationId,
            request.RoleId,
            cancellationToken))
        {
            return OrganizationErrors.AppRoleNotAssigned(request.UserId, applicationId, request.RoleId);
        }

        await _organizationRepository.RemoveAppRoleAsync(
            request.OrganizationId,
            request.UserId,
            applicationId,
            request.RoleId,
            cancellationToken);

        _logger.LogInformation(
            "Role {RoleId} removed from user {UserId} for app {ApplicationId} in org {OrganizationId} by {RemovedBy}",
            request.RoleId, request.UserId, applicationId, request.OrganizationId, request.RemovedBy);

        return Result.Deleted;
    }
}
