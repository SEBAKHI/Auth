using System.Globalization;
using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

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
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly INotificationService _notificationService;
    private readonly EmailSettings _emailSettings;
    private readonly ILogger<ResendInvitationCommandHandler> _logger;

    public ResendInvitationCommandHandler(
        IOrganizationRepository organizationRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IRefreshTokenKeyService tokenKeyService,
        INotificationService notificationService,
        IOptionsSnapshot<EmailSettings> emailSettings,
        ILogger<ResendInvitationCommandHandler> logger)
    {
        _organizationRepository = organizationRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _tokenKeyService = tokenKeyService;
        _notificationService = notificationService;
        _emailSettings = emailSettings.Value;
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

        // Only the hash is stored; newToken goes into the email and into the DTO
        // returned to the administrator who asked for the resend. Regenerating also
        // invalidates the previous token, because the old hash is overwritten.
        var newToken = _tokenGenerator.Generate();
        var result = invitation.RegenerateToken(_tokenKeyService.ComputeTokenHash(newToken));
        if (result.IsError)
        {
            return result.Errors;
        }

        await _organizationRepository.UpdateInvitationAsync(invitation, cancellationToken);

        _logger.LogInformation(
            "Invitation {InvitationId} resent for {Email} to organization {OrganizationId} by {ResentBy}",
            invitation.Id, EmailMasking.Mask(invitation.Email.Value), request.OrganizationId, request.ResentBy);

        // Build response DTO
        var organization = await _organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        var role = await _roleRepository.GetByIdAsync(invitation.RoleId, cancellationToken);
        var inviter = await _userRepository.GetByIdAsync(invitation.InvitedBy, cancellationToken);

        var inviterName = inviter != null
            ? $"{inviter.FirstName} {inviter.LastName}".Trim()
            : "An administrator";

        // Send from the database-managed template; the invitee's profile language
        // (matched by email inside the service) wins over the resender's culture.
        var sendResult = await _notificationService.SendAsync(
            new NotificationRequest
            {
                TypeCode = NotificationTypeCodes.OrganizationInvitation,
                RecipientAddress = invitation.Email.Value,
                TriggeredBy = request.ResentBy,
                LanguageHint = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                Variables = new Dictionary<string, object?>
                {
                    ["OrganizationName"] = organization?.Name ?? string.Empty,
                    ["InviterName"] = inviterName,
                    ["InvitationLink"] = _emailSettings.BuildFrontendUrl(
                        $"/accept-invitation?token={Uri.EscapeDataString(newToken)}"),
                    ["InvitationToken"] = newToken,
                    ["ExpiresAt"] = invitation.ExpiresAt
                }
            },
            cancellationToken);

        // Email failure must not fail the command. The regenerated invitation
        // stands and can be resent again.
        //
        // The regenerated plaintext is NOT returned to the caller, for the reason
        // given at length in InviteMemberCommandHandler: redeeming it confirms an
        // address with no further proof, so it may exist only in the mailbox it
        // was sent to. This path mattered as much as the mint — it hands out a
        // freshly minted token under the same permission, so leaving it behind
        // would have left a one-call route to exactly what was closed there.
        if (sendResult.IsError)
        {
            _logger.LogWarning(
                "Failed to send invitation email for resent invitation {InvitationId}: {Error}; the invitation stands and can be resent",
                invitation.Id, sendResult.FirstError.Description);
        }

        return new OrganizationInvitationDto
        {
            Id = invitation.Id,
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
