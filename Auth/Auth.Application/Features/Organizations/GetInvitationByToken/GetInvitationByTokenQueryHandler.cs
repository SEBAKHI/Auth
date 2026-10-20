using Auth.Application.DTOs;
using Auth.Application.Interfaces;
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
    private readonly IApplicationRepository _applicationRepository;
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetInvitationByTokenQueryHandler> _logger;

    public GetInvitationByTokenQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IApplicationRepository applicationRepository,
        IRefreshTokenKeyService tokenKeyService,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetInvitationByTokenQueryHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _applicationRepository = applicationRepository;
        _tokenKeyService = tokenKeyService;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<InvitationPreviewDto>> Handle(
        GetInvitationByTokenQuery request,
        CancellationToken cancellationToken)
    {
        var invitation = await _organizationRepository.GetInvitationByTokenHashAsync(
            _tokenKeyService.ComputeTokenHash(request.Token), cancellationToken);
        if (invitation == null)
        {
            return OrganizationErrors.InvitationNotFoundByToken;
        }

        var organization = await _organizationRepository.GetByIdAsync(invitation.OrganizationId, cancellationToken);
        var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);
        var inviter = await _userRepository.GetByIdAsync(invitation.InvitedBy, cancellationToken);

        var userExists = await _userRepository.ExistsByEmailAsync(invitation.Email.Value, cancellationToken);

        // The application whose role the invitation carries, so the invitee sees where
        // accepting will take them (OI-192). Its role is read only when the application is
        // still there: a role without its application names nothing the invitee can use.
        var application = invitation.ApplicationRole is { } named
            ? await _applicationRepository.GetByIdAsync(named.ApplicationId, cancellationToken)
            : null;
        var applicationRole = application is not null
            ? await _roleRepository.GetByIdAsync(invitation.ApplicationRole!.RoleId, cancellationToken)
            : null;

        _logger.LogInformation(
            "Invitation {InvitationId} previewed for organization {OrganizationId}",
            invitation.Id, invitation.OrganizationId);

        return new InvitationPreviewDto
        {
            Id = invitation.Id,
            OrganizationName = organization?.Name ?? string.Empty,
            OrganizationLogoUrl = _imageUrlComposer.Compose(organization?.LogoUrl),
            Email = invitation.Email.Value,
            RoleName = role?.Name ?? string.Empty,
            InvitedByName = inviter != null ? $"{inviter.FirstName} {inviter.LastName}".Trim() : string.Empty,
            Status = invitation.Status.ToString(),
            ExpiresAt = invitation.ExpiresAt,
            IsExpired = invitation.IsExpired(),
            UserExists = userExists,
            ApplicationName = application?.Name,
            ApplicationLogoUrl = _imageUrlComposer.Compose(application?.LogoUrl),
            ApplicationLogoUrlDark = _imageUrlComposer.Compose(application?.LogoUrlDark),
            ApplicationRoleName = applicationRole?.Name
        };
    }
}
