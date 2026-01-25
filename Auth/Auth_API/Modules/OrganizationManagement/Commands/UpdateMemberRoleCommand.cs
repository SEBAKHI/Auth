using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Command to update a member's organization role.
/// </summary>
public record UpdateMemberRoleCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid NewRoleId) : IRequest<ErrorOr<OrganizationMemberDto>>
{
    /// <summary>
    /// The ID of the user performing the update.
    /// </summary>
    public Guid ModifiedBy { get; set; }
}
