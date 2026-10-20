using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// Response for registration through an organization invitation.
/// </summary>
/// <remarks>
/// It carries the same result as accepting while signed in, so the page can sign the new
/// account in and send it on exactly as it would an existing one. It issues no session:
/// the page signs in through the ordinary login call with the password just chosen.
/// </remarks>
/// <param name="UserId">The newly created user's ID.</param>
/// <param name="Email">The registered email address (the invited email).</param>
/// <param name="OrganizationName">Name of the organization joined.</param>
/// <param name="RoleName">Name of the membership role granted.</param>
/// <param name="Message">Human-readable result message.</param>
/// <param name="OrganizationId">The organization joined.</param>
/// <param name="ApplicationRoleOutcome">As <see cref="InvitationAcceptResultDto.ApplicationRoleOutcome"/>.</param>
/// <param name="Application">As <see cref="InvitationAcceptResultDto.Application"/>.</param>
/// <param name="LaunchUrl">As <see cref="InvitationAcceptResultDto.LaunchUrl"/>.</param>
public record RegisterWithInvitationResponse(
    Guid UserId,
    string Email,
    string OrganizationName,
    string RoleName,
    string Message,
    Guid OrganizationId,
    InvitationApplicationRoleOutcome? ApplicationRoleOutcome,
    InvitationApplicationDto? Application,
    string? LaunchUrl);
