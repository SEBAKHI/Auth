using Asp.Versioning;
using Auth_API.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPasswordPolicy;
using Auth.Application.Features.Platform.GetPlatformBranding;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Administration.Controllers;

/// <summary>
/// Public platform endpoints. Branding is served anonymously because the
/// login and invitation screens render the platform name/logo before any
/// authentication happens; the password policy for the same reason, since
/// sign-up, invitation and reset forms take a new password before there is a
/// session to authenticate with.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
public class PlatformController : ApiController
{
    private readonly ISender _sender;

    public PlatformController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Gets the public platform branding (name and logo URL).
    /// </summary>
    /// <response code="200">Returns the platform branding</response>
    [HttpGet("branding")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PlatformBrandingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBranding(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPlatformBrandingQuery(), cancellationToken);

        return result.Match(
            branding => Ok(branding),
            errors => Problem(errors));
    }

    /// <summary>
    /// Gets the composition rules a new password must satisfy.
    /// </summary>
    /// <remarks>
    /// Only the rules a person can act on while typing are disclosed (see
    /// <see cref="PasswordPolicyDto"/>). The server still judges every
    /// submission and may refuse on grounds this payload does not list —
    /// common patterns, breached passwords, password history — so a client
    /// that satisfies this list must still show whatever the submission returns.
    /// </remarks>
    /// <response code="200">Returns the public password policy</response>
    [HttpGet("password-policy")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PasswordPolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPasswordPolicy(CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPasswordPolicyQuery(), cancellationToken);

        // Identical for every caller, so a browser or the gateway may reuse it —
        // but only briefly: the policy is edited live from the console, and an
        // operator's change has to reach the next visitor within the minute,
        // not after a day of serving the old minimum.
        Response.Headers.CacheControl = "public, max-age=60";

        return result.Match(
            policy => Ok(policy),
            errors => Problem(errors));
    }
}
