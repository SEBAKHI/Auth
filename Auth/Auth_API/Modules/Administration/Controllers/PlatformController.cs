using Asp.Versioning;
using Auth_API.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPasswordPolicy;
using Auth.Application.Features.Platform.GetPlatformBranding;
using Auth.Application.Features.LoginDesigns.GetPublicLoginDesign;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    /// Gets the public platform branding (name, logo URLs, appearance) and
    /// whether the sign-in page offers "Stay signed in" (staySignedInOffered).
    /// </summary>
    /// <remarks>
    /// Only that boolean is published, never the administrator's
    /// <c>IdentityProvider:StaySignedInMode</c>: the page acts on nothing else,
    /// and the server applies the setting to every sign-in whatever the page sends.
    /// </remarks>
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
    /// The design the platform's own sign-in pages show in their panel, rendered for one
    /// language. 404 when none is active (the plain panel).
    /// </summary>
    /// <remarks>
    /// Anonymous, like the branding beside it: the sign-in pages call it before anyone signs in,
    /// and only when the branding names a design. The HTML travels inside JSON, never as a
    /// document of the API's origin, which must not serve markup an administrator wrote. The
    /// page shows it in an isolated frame. Rendering is page rendering: the per-address sign-in-page allowance.
    /// </remarks>
    /// <response code="200">Returns the rendered design</response>
    [HttpGet("login-design")]
    [AllowAnonymous]
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(typeof(PublicLoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLoginDesign([FromQuery] string? lang, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetPublicLoginDesignQuery(null, lang), cancellationToken);

        return result.Match(
            design => Ok(design),
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
