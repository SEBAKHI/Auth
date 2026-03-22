using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.ResendInvitation;

/// <summary>
/// Handler for resending an organization invitation with a regenerated token.
/// </summary>
public class ResendInvitationCommandHandler : IRequestHandler<ResendInvitationCommand, ErrorOr<OrganizationInvitationDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly ILogger<ResendInvitationCommandHandler> _logger;

    public ResendInvitationCommandHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        ILogger<ResendInvitationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _logger = logger;
    }

    public async Task<ErrorOr<OrganizationInvitationDto>> Handle(
        ResendInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var invitation = await _organizationRepository.GetInvitationByIdAsync(request.InvitationId, cancellationToken);
        if (invitation is null)
        {
            return OrganizationErrors.InvitationNotFound(request.InvitationId);
        }

        if (invitation.OrganizationId != request.OrganizationId)
        {
            return OrganizationErrors.InvitationNotFound(request.InvitationId);
        }

        var newToken = _tokenGenerator.Generate();
        var result = invitation.RegenerateToken(newToken);
        if (result.IsError)
        {
            return result.Errors;
        }

        await _organizationRepository.UpdateInvitationAsync(invitation, cancellationToken);

        _logger.LogInformation(
            "Invitation {InvitationId} resent for {Email} to organization {OrganizationId} by {ResentBy}",
            invitation.Id, invitation.Email.Value, request.OrganizationId, request.ResentBy);

        // Build response DTO
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);
        var inviter = await _userRepository.GetByIdAsync(invitation.InvitedBy, cancellationToken);

        return new OrganizationInvitationDto
        {
            Id = invitation.Id,
            Token = newToken,
            OrganizationId = invitation.OrganizationId,
            OrganizationName = organization?.Name ?? string.Empty,
            Email = invitation.Email.Value,
            RoleId = invitation.RoleId,
            RoleCode = role?.Code ?? string.Empty,
            RoleName = role?.Name ?? string.Empty,
            Status = invitation.Status.ToString(),
            ExpiresAt = invitation.ExpiresAt,
            IsExpired = invitation.IsExpired(),
            InvitedBy = invitation.InvitedBy,
            InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : null,
            InvitedByEmail = inviter?.Email?.Value,
            AcceptedAt = invitation.AcceptedAt,
            AcceptedByUserId = invitation.AcceptedByUserId,
            CreatedAt = invitation.CreatedAt
        };
    }
}
