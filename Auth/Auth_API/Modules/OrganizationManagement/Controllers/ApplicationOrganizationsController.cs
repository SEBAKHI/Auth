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
/// The application directory (OI-194): an application's server reads, with a user's own access
/// token, the organizations that user belongs to and that enabled the application — the
/// organization, its members and the application's own invitations.
/// </summary>
/// <remarks>
/// <para>
/// The second and last place the application-token scheme is named (the first is userinfo), and
/// only here, at class level. Nothing else guards these actions on purpose: no permission
/// attribute, which would read the application token's own <c>permissions</c> claim, where an
/// application may name a code in a platform namespace; no anonymous access; no platform scope.
/// What the caller may read is decided by <see cref="ApplicationDirectoryAccess"/> from their
/// membership at the call.
/// </para>
/// <para>
/// The token is read from the Authorization header only. A user or application that can no
/// longer be served is refused exactly as a revoked token is; a missing scope is a 403 with
/// <c>error="insufficient_scope"</c>; every organization that is not the caller's to read is the
/// same 404.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/app/organizations")]
[Authorize(AuthenticationSchemes = AccessTokenValidation.UserInfoScheme)]
[EnableRateLimiting(ApplicationReadClassificationMiddleware.PolicyName)]
public sealed class ApplicationOrganizationsController : ApplicationDirectoryController
{
    /// <summary>RFC 6750 §3.1: the scope the request would have needed.</summary>
    protected override string InsufficientScopeChallenge { get; } =
        $"Bearer error=\"insufficient_scope\", scope=\"{OAuthScopes.OrganizationsRead}\"";

    private readonly ISender _sender;

    public ApplicationOrganizationsController(ISender sender)
    {
        _sender = sender;
    }

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
            new GetApplicationOrganizationQuery(ApplicationDirectoryCaller.FromPrincipal(User), orgId),
            cancellationToken));

    /// <summary>
    /// One page of the organization's active members, oldest membership first. Requires
    /// <c>org:members:read</c>; each member's roles in the calling application are included when
    /// the caller also holds <c>org:permissions:read</c>.
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
            new GetApplicationOrganizationMembersQuery(ApplicationDirectoryCaller.FromPrincipal(User), orgId, pageNumber, pageSize),
            cancellationToken));

    /// <summary>
    /// One page of the invitations that carry the calling application's role, newest first.
    /// Requires <c>org:members:invite</c>. <paramref name="status"/> is repeatable
    /// (<c>?status=Pending&amp;status=Expired</c>), one exact status name per value; without it,
    /// <c>Pending</c> and <c>Expired</c>.
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
                ApplicationDirectoryCaller.FromPrincipal(User), orgId, status, pageNumber, pageSize),
            cancellationToken));
}
