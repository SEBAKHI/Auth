using Asp.Versioning;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Domain.Constants;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
public sealed class ApplicationOrganizationsController : ApiController
{
    /// <summary>RFC 6750 §3.1: the scope the request would have needed.</summary>
    private static readonly string InsufficientScopeChallenge =
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

    /// <summary>
    /// The answer for a directory result: 200; the revoked-token 401 for a caller that can no
    /// longer be served; the scope challenge with a missing scope's 403; otherwise the error
    /// contract's problem. Every answer on these endpoints carries <c>Cache-Control: no-store</c>,
    /// set before the limiter by <see cref="ApplicationReadClassificationMiddleware"/>, so the 401
    /// written here and the blacklist's carry the same headers.
    /// </summary>
    private IActionResult Answer<TValue>(ErrorOr<TValue> result)
    {
        if (result.IsError && result.FirstError.Code == ApplicationDirectoryRefusals.CallerNotServable.Code)
        {
            BearerTokenRejection.Reject(HttpContext, ChallengeReasonCodes.TokenRevoked);
            return new EmptyResult();
        }

        if (!result.IsError)
        {
            return Ok(result.Value);
        }

        var first = result.FirstError;

        if (first.Code == ApplicationDirectoryRefusals.InsufficientScope.Code)
        {
            Response.Headers.WWWAuthenticate = InsufficientScopeChallenge;
        }

        return Problem(result.Errors);
    }

    /// <summary>
    /// Binds a query parameter exactly as the request carried it: a string, or every value of a
    /// repeated parameter, with no conversion and no failure. Nothing about the query can be
    /// refused before the action runs, so the handler answers "who are you" first and reads the
    /// values after it (<see cref="ApplicationDirectoryPaging"/>). An absent parameter is null; an
    /// empty one is the empty string, which the handler refuses, rather than null.
    /// </summary>
    internal sealed class RawQueryValuesBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (values == ValueProviderResult.None)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
                return Task.CompletedTask;
            }

            object model = bindingContext.ModelType == typeof(string[])
                ? values.Values.Select(value => value ?? string.Empty).ToArray()
                : values.FirstValue ?? string.Empty;
            bindingContext.Result = ModelBindingResult.Success(model);
            return Task.CompletedTask;
        }
    }
}
