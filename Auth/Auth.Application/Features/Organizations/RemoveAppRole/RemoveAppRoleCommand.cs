using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.RemoveAppRole;

/// <summary>
/// Command to remove an app-level role from a user within an organization.
/// The application is derived from the role, since a role belongs to exactly
/// one application.
/// </summary>
public record RemoveAppRoleCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid RoleId) : IRequest<ErrorOr<Deleted>>
{
    /// <summary>
    /// The ID of the user removing the role.
    /// </summary>
    public Guid RemovedBy { get; init; }
}
