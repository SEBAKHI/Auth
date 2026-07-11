using Auth.Application.DTOs;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetInvitationByToken;

/// <summary>
/// Handler for previewing an organization invitation by token.
/// Returns the preview even for expired/accepted invitations so the caller
/// can render the exact state; only an unknown token is an error.
/// </summary>
public class GetInvitationByTokenQueryHandler : IRequestHandler<GetInvitationByTokenQuery, ErrorOr<InvitationPreviewDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILogger<GetInvitationByTokenQueryHandler> _logger;

    public GetInvitationByTokenQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILogger<GetInvitationByTokenQueryHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<InvitationPreviewDto>> Handle(
        GetInvitationByTokenQuery request,
        CancellationToken cancellationToken)
    {
        var invitation = await _organizationRepository.GetInvitationByTokenAsync(request.Token, cancellationToken);
        if (invitation == null)
        {
            return OrganizationErrors.InvitationNotFoundByToken;
        }

        var organization = await _organizationRepository.GetByIdAsync(invitation.OrganizationId, cancellationToken);
        var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);
        var inviter = await _userRepository.GetByIdAsync(invitation.InvitedBy, cancellationToken);

        var userExists = await _userRepository.ExistsByEmailAsync(invitation.Email.Value, cancellationToken);

        _logger.LogInformation(
            "Invitation {InvitationId} previewed for organization {OrganizationId}",
            invitation.Id, invitation.OrganizationId);

        return new InvitationPreviewDto
        {
            Id = invitation.Id,
            OrganizationName = organization?.Name ?? string.Empty,
            OrganizationLogoUrl = organization?.LogoUrl,
            Email = invitation.Email.Value,
            RoleName = role?.Name ?? string.Empty,
            InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : string.Empty,
            Status = invitation.Status.ToString(),
            ExpiresAt = invitation.ExpiresAt,
            IsExpired = invitation.IsExpired(),
            UserExists = userExists
        };
    }
}
