using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Command to enable an application for an organization.
/// </summary>
public record EnableApplicationCommand(
    Guid OrganizationId,
    Guid ApplicationId,
    string? SubscriptionTier = null,
    DateTime? ExpiresAt = null) : IRequest<ErrorOr<OrganizationApplicationDto>>
{
    /// <summary>
    /// The ID of the user enabling the application.
    /// </summary>
    public Guid EnabledBy { get; set; }
}
