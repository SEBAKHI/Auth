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
    public Guid CreatedBy { get; init; }

    /// <summary>
    /// True when the caller acts as a platform administrator rather than as a
    /// user creating an organization for themselves.
    /// </summary>
    /// <remarks>
    /// Set at the edge from the caller's <c>organizations:manage</c> claim, the
    /// same way <c>DeleteOrganizationCommand</c> does. It widens scope past the
    /// self-service switch; it is not an endpoint gate, and nothing here treats
    /// it as one.
    /// </remarks>
    public bool PlatformScope { get; init; }
}
