namespace Auth.Application.DTOs;

/// <summary>
/// Response for registration through an organization invitation.
/// </summary>
/// <param name="UserId">The newly created user's ID.</param>
/// <param name="Email">The registered email address (the invited email).</param>
/// <param name="OrganizationName">Name of the organization joined.</param>
/// <param name="RoleName">Name of the membership role granted.</param>
/// <param name="Message">Human-readable result message.</param>
public record RegisterWithInvitationResponse(
    Guid UserId,
    string Email,
    string OrganizationName,
    string RoleName,
    string Message);
