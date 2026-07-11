using Asp.Versioning;
using Auth_API.Common;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.GetPlatformBranding;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Administration.Controllers;

/// <summary>
/// Public platform endpoints. Branding is served anonymously because the
/// login and invitation screens render the platform name/logo before any
/// authentication happens.
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
}
