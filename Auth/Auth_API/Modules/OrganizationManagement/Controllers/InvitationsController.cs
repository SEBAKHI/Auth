using Asp.Versioning;
using Auth_API.Common;
using Auth_API.Modules.OrganizationManagement.Contracts;
using Auth.Application.Features.Organizations.AcceptInvitation;
using Auth.Application.Features.Organizations.GetInvitationByToken;
using Auth.Application.Features.Organizations.RegisterWithInvitation;
using Auth.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    /// Preview an organization invitation by its token.
    /// Anonymous: possession of the emailed single-use token is the authorization.
    /// </summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(InvitationPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetInvitationByToken(string token, CancellationToken cancellationToken)
    {
        var query = new GetInvitationByTokenQuery(token);
        var result = await _sender.Send(query, cancellationToken);

        return result.Match(
            preview => Ok(preview),
            errors => Problem(errors));
    }

    /// <summary>
    /// Register a new account through an organization invitation and accept it.
    /// The email comes from the invitation; the account is created with a
    /// confirmed email (token possession proves mailbox ownership).
    /// </summary>
    [HttpPost("{token}/register")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(RegisterWithInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RegisterWithInvitation(
        string token,
        [FromBody] RegisterWithInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RegisterWithInvitationCommand(
            token,
            request.Password,
            request.FirstName,
            request.LastName,
            request.PreferredLanguage,
            request.TimeZone);
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            response => Ok(response),
            errors => Problem(errors));
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
    public async Task<IActionResult> AcceptInvitation(string token, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var command = new AcceptInvitationCommand(token) { AcceptedBy = userId };
        var result = await _sender.Send(command, cancellationToken);

        return result.Match(
            acceptResult => Ok(acceptResult),
            errors => Problem(errors));
    }

}
