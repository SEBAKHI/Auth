using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.UpdateMemberRole;

/// <summary>
/// Handler for updating a member's organization role.
/// </summary>
public class UpdateMemberRoleCommandHandler : IRequestHandler<UpdateMemberRoleCommand, ErrorOr<OrganizationMemberDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<UpdateMemberRoleCommandHandler> _logger;

    public UpdateMemberRoleCommandHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ILogger<UpdateMemberRoleCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationMemberDto>> Handle(
        UpdateMemberRoleCommand request,
        CancellationToken cancellationToken)
    {
        // Get organization
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization == null)
        {
            return OrganizationErrors.NotFound(request.OrganizationId);
        }

        // Cannot change own role
        if (request.UserId == request.ModifiedBy)
        {
            return OrganizationErrors.CannotChangeOwnRole;
        }

        // Validate role exists
        var role = await _roleRepository.GetByIdAsync(request.NewRoleId, cancellationToken);
        if (role == null)
        {
            return OrganizationErrors.RoleNotFound(request.NewRoleId);
        }

        // The membership role must be organization-level; app roles are assigned separately
        if (role.ApplicationId != null)
        {
            return OrganizationErrors.InvalidMembershipRole(request.NewRoleId);
        }

        // Get membership
        var membership = await _organizationRepository.GetMembershipAsync(
            request.OrganizationId,
            request.UserId,
            cancellationToken);

        if (membership == null)
        {
            return OrganizationErrors.NotMember(request.UserId, request.OrganizationId);
        }

        // Update role
        membership.UpdateRole(request.NewRoleId, request.ModifiedBy);
        await _organizationRepository.UpdateMemberAsync(membership, cancellationToken);

        // Get user info for response
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        var inviter = await _userRepository.GetByIdAsync(membership.InvitedBy, cancellationToken);

        _logger.LogInformation(
            "Member {UserId} role updated to {RoleId} in organization {OrganizationId} by {ModifiedBy}",
            request.UserId, request.NewRoleId, request.OrganizationId, request.ModifiedBy);

        return new OrganizationMemberDto
        {
            Id = membership.Id,
            OrganizationId = membership.OrganizationId,
            UserId = membership.UserId,
            Email = user?.Email ?? string.Empty,
            FirstName = user?.FirstName,
            LastName = user?.LastName,
            FullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : null,
            RoleId = membership.RoleId,
            RoleCode = role.Code,
            RoleName = role.Name,
            IsActive = membership.IsActive,
            JoinedAt = membership.JoinedAt,
            InvitedBy = membership.InvitedBy,
            InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : null,
            ExpiresAt = membership.ExpiresAt
        };
    }
}
