using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Organizations.UpdateOrganizationApplication;

/// <summary>
/// Command to update an organization's application subscription settings.
/// </summary>
public record UpdateOrganizationApplicationCommand(
    Guid OrganizationId,
    Guid ApplicationId,
    string? SubscriptionTier,
    DateTime? ExpiresAt,
    bool? IsActive) : IRequest<ErrorOr<OrganizationApplicationDto>>
{
    /// <summary>
    /// The user updating the application.
    /// </summary>
    public Guid ModifiedBy { get; init; }
}
