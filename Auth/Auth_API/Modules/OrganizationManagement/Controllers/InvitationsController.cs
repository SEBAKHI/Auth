using Asp.Versioning;
using Auth_Lib.Application.Features.Organizations.AcceptInvitation;
using Auth_Lib.Application.DTOs;
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
public class InvitationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InvitationsController(IMediator mediator)
    {
        _mediator = mediator;
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
        var result = await _mediator.Send(command);

        return result.Match(
            acceptResult => Ok(acceptResult),
            errors => errors.First().Type switch
            {
                ErrorOr.ErrorType.NotFound => NotFound(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Conflict => Conflict(new { error = errors.First().Description }),
                ErrorOr.ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = errors.First().Description }),
                _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: errors.First().Description)
            });
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
