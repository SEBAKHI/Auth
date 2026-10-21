using Asp.Versioning;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Domain.Constants;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth_API.Modules.OrganizationManagement.Controllers;

/// <summary>
/// The application directory read by the application itself (OI-202): its own server, with its
/// machine key and no user, reads the organizations that enabled it — the organization, the
/// members who hold one of its roles, and its own invitations.
/// </summary>
/// <remarks>
/// <para>
/// The one place the machine scheme is named, at class level. Nothing else guards these actions
/// on purpose: no permission attribute, which would read a token claim instead of the database; no
/// anonymous access; no platform scope; no other scheme. What the application may read is decided
/// by <see cref="ApplicationDirectoryAccess"/>: the machine scope in the token, the application's
/// consent now, and the organization's enablement of the application.
/// </para>
/// <para>
/// The same answers as the user's door (<see cref="ApplicationOrganizationsController"/>): an
/// application that is gone or inactive is refused exactly as a revoked token is; a missing scope
/// or a withdrawn consent is a 403 with <c>error="insufficient_scope"</c> naming
/// <c>organizations.read.all</c>; every organization that is not the application's to read is the
/// same 404.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/app/machine/organizations")]
[Authorize(AuthenticationSchemes = AccessTokenValidation.MachineScheme)]
[EnableRateLimiting(ApplicationReadClassificationMiddleware.PolicyName)]
[MachineApplicationRead]
public sealed class ApplicationMachineOrganizationsController : ApplicationDirectoryController
{
    private readonly ISender _sender;

    public ApplicationMachineOrganizationsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>RFC 6750 §3.1: the scope the request would have needed.</summary>
    protected override string InsufficientScopeChallenge { get; } =
        $"Bearer error=\"insufficient_scope\", scope=\"{MachineClients.Scopes.OrganizationsReadAll}\"";

    /// <summary>The organization: its id, name and logo.</summary>
    [HttpGet("{orgId:guid}")]
    [SameAnswerAsBlacklist]
    [ProducesResponseType(typeof(ApplicationOrganizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetOrganization(Guid orgId, CancellationToken cancellationToken) =>
        Answer(await _sender.Send(
            new GetApplicationOrganizationQuery(ApplicationMachineCaller.FromPrincipal(User), orgId),
            cancellationToken));

    /// <summary>
    /// One page of the organization's active members who hold at least one of this application's
    /// roles in it, oldest membership first, each with those roles.
    /// </summary>
    [HttpGet("{orgId:guid}/members")]
    [SameAnswerAsBlacklist]
    [ProducesResponseType(typeof(ApplicationDirectoryPage<ApplicationOrganizationMemberResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetMembers(
        Guid orgId,
        [FromQuery][ModelBinder(typeof(RawQueryValuesBinder))] string? pageNumber = null,
        [FromQuery][ModelBinder(typeof(RawQueryValuesBinder))] string? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Answer(await _sender.Send(
            new GetApplicationOrganizationMembersQuery(ApplicationMachineCaller.FromPrincipal(User), orgId, pageNumber, pageSize),
            cancellationToken));

    /// <summary>
    /// One page of the invitations that carry this application's role, newest first.
    /// <paramref name="status"/> is repeatable (<c>?status=Pending&amp;status=Expired</c>), one
    /// exact status name per value; without it, <c>Pending</c> and <c>Expired</c>.
    /// </summary>
    [HttpGet("{orgId:guid}/invitations")]
    [SameAnswerAsBlacklist]
    [ProducesResponseType(typeof(ApplicationDirectoryPage<ApplicationOrganizationInvitationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetInvitations(
        Guid orgId,
        [FromQuery][ModelBinder(typeof(RawQueryValuesBinder))] string[]? status = null,
        [FromQuery][ModelBinder(typeof(RawQueryValuesBinder))] string? pageNumber = null,
        [FromQuery][ModelBinder(typeof(RawQueryValuesBinder))] string? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Answer(await _sender.Send(
            new GetApplicationOrganizationInvitationsQuery(
                ApplicationMachineCaller.FromPrincipal(User), orgId, status, pageNumber, pageSize),
            cancellationToken));
}
