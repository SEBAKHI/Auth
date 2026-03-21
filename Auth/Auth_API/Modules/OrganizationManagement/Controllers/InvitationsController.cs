using Asp.Versioning;
using Auth_API.Common;
using Auth.Application.Features.Organizations.AcceptInvitation;
using Auth.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.OrganizationManagement.Controllers;

/// <summary>
/// Controller for invitation acceptance operations.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class InvitationsController : ApiController
{
    private readonly ISender _sender;

    public InvitationsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Accept an organization invitation.
    /// </summary>
    [HttpPost("{token}/accept")]
    [Authorize]
    [ProducesResponseType(typeof(InvitationAcceptResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptInvitation(string token)
    {
        var userId = GetCurrentUserId();
        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };
        var result = await _sender.Send(command);

        return result.Match(
            acceptResult => Ok(acceptResult),
            errors => Problem(errors));
    }

}
