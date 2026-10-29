using Asp.Versioning;
using Auth_API.Authorization;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth_API.Modules.ApplicationManagement.Controllers;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.Administration.Controllers;

/// <summary>
/// The platform's sign-in page designs (the console's platform settings): the design the
/// platform's own sign-in pages show, and every application's without an active design of its
/// own. The same rules as an application's designs; every endpoint needs
/// <c>platform-settings:manage</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/platform-settings/login-designs")]
[Authorize]
[RequirePermission(PermissionCodes.PlatformSettings.Manage)]
public class PlatformLoginDesignsController : LoginDesignEndpoints
{
    public PlatformLoginDesignsController(ISender sender) : base(sender)
    {
    }

    /// <summary>The platform's designs, the active one first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LoginDesignSummaryDto>), StatusCodes.Status200OK)]
    public Task<IActionResult> GetDesigns(CancellationToken cancellationToken) =>
        List(null, cancellationToken);

    /// <summary>One design in full, for its editor.</summary>
    [HttpGet("{designId:guid}")]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetDesign(Guid designId, CancellationToken cancellationToken) =>
        Get(null, designId, cancellationToken);

    /// <summary>Creates a design. It starts inactive.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> CreateDesign([FromBody] CreateLoginDesignRequest request, CancellationToken cancellationToken) =>
        Create(null, request, nameof(GetDesign), cancellationToken);

    /// <summary>Saves a design; on the active design the save is live.</summary>
    [HttpPut("{designId:guid}")]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UpdateDesign(
        Guid designId,
        [FromBody] UpdateLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        Update(null, designId, request, cancellationToken);

    /// <summary>Copies a design into a new, inactive one.</summary>
    [HttpPost("{designId:guid}/copy")]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status201Created)]
    public Task<IActionResult> CopyDesign(
        Guid designId,
        [FromBody] CopyLoginDesignRequest request,
        CancellationToken cancellationToken) =>
        Copy(null, designId, request, nameof(GetDesign), cancellationToken);

    /// <summary>Makes a design the one the platform's sign-in pages show.</summary>
    [HttpPost("{designId:guid}/activate")]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> ActivateDesign(Guid designId, CancellationToken cancellationToken) =>
        Activate(null, designId, cancellationToken);

    /// <summary>Stops showing a design: the platform's pages fall back to the plain panel.</summary>
    [HttpPost("{designId:guid}/deactivate")]
    [ProducesResponseType(typeof(LoginDesignDto), StatusCodes.Status200OK)]
    public Task<IActionResult> DeactivateDesign(Guid designId, CancellationToken cancellationToken) =>
        Deactivate(null, designId, cancellationToken);

    /// <summary>Deletes an inactive design; the active one is refused (409).</summary>
    [HttpDelete("{designId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> DeleteDesign(Guid designId, CancellationToken cancellationToken) =>
        Delete(null, designId, cancellationToken);

    /// <summary>Renders a design as the editor holds it, without saving it.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(LoginDesignPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> PreviewDesign([FromBody] PreviewLoginDesignRequest request, CancellationToken cancellationToken) =>
        Preview(null, request, cancellationToken);
}
