using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.UpdateOrganization;

/// <summary>
/// Command to update an existing organization.
/// </summary>
public record UpdateOrganizationCommand(
    Guid OrganizationId,
    string Name,
    string ContactEmail,
    string? Description = null,
    string? LogoUrl = null,
    string? Website = null,
    bool? IsActive = null) : IRequest<ErrorOr<OrganizationDto>>
{
    /// <summary>
    /// The ID of the user performing the update.
    /// </summary>
    public Guid ModifiedBy { get; set; }
}
