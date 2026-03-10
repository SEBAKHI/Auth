using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.CreateOrganization;

/// <summary>
/// Command to create a new organization.
/// The creating user becomes the owner of the organization.
/// </summary>
public record CreateOrganizationCommand(
    string Code,
    string Name,
    string ContactEmail,
    string? Description = null,
    string? LogoUrl = null,
    string? Website = null) : IRequest<ErrorOr<OrganizationDto>>
{
    /// <summary>
    /// The ID of the user creating this organization (becomes owner).
    /// </summary>
    public Guid CreatedBy { get; set; }
}
