using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.GrantPermission;

/// <summary>
/// Handler for granting an individual permission to a user within an organization.
/// </summary>
public class GrantPermissionCommandHandler : IRequestHandler<GrantPermissionCommand, ErrorOr<OrganizationMemberPermissionDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<GrantPermissionCommandHandler> _logger;

    public GrantPermissionCommandHandler(
        IOrganizationRepository organizationRepository,
        IApplicationRepository applicationRepository,
        IPermissionRepository permissionRepository,
        IUserRepository userRepository,
        ILogger<GrantPermissionCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _applicationRepository = applicationRepository;
        _permissionRepository = permissionRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationMemberPermissionDto>> Handle(
        GrantPermissionCommand request,
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

        // Check permission exists and belongs to the application
        var permission = await _permissionRepository.GetByIdAsync(request.PermissionId, cancellationToken);
        if (permission == null)
        {
            return OrganizationErrors.PermissionNotFound(request.PermissionId);
        }

        if (permission.ApplicationId != request.ApplicationId)
        {
            return OrganizationErrors.PermissionNotForApplication(request.PermissionId, request.ApplicationId);
        }

        // Check if already granted
        if (await _organizationRepository.HasPermissionAsync(
            request.OrganizationId,
            request.UserId,
            request.ApplicationId,
            request.PermissionId,
            cancellationToken))
        {
            return OrganizationErrors.PermissionAlreadyGranted(request.UserId, request.ApplicationId, request.PermissionId);
        }

        // Create permission grant
        var grant = OrganizationUserPermission.Create(
            request.OrganizationId,
            request.UserId,
            request.ApplicationId,
            request.PermissionId,
            request.GrantedBy,
            request.ExpiresAt);

        await _organizationRepository.GrantPermissionAsync(grant, cancellationToken);

        // Get granter info
        var grantedByUser = await _userRepository.GetByIdAsync(request.GrantedBy, cancellationToken);

        _logger.LogInformation(
            "Permission {PermissionId} granted to user {UserId} for app {ApplicationId} in org {OrganizationId} by {GrantedBy}",
            request.PermissionId, request.UserId, request.ApplicationId, request.OrganizationId, request.GrantedBy);

        return new OrganizationMemberPermissionDto
        {
            Id = grant.Id,
            ApplicationId = grant.ApplicationId,
            ApplicationCode = application.Code,
            ApplicationName = application.Name,
            PermissionId = grant.PermissionId,
            PermissionCode = permission.Code,
            PermissionName = permission.Name,
            GrantedAt = grant.GrantedAt,
            GrantedBy = grant.GrantedBy,
            GrantedByName = grantedByUser != null ? $"{grantedByUser.FirstName} {grantedByUser.LastName}".Trim() : null,
            ExpiresAt = grant.ExpiresAt
        };
    }
}
