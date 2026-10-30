using Asp.Versioning;
using Auth.Application.DTOs;
using Auth.Application.Features.ApiKeys.ValidateApiKey;
using Auth.Application.Features.Authentication.IntrospectToken;
using Auth.Application.Features.MachineCredentialChecks;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Application.Features.WebhookKeys.ValidateWebhookKey;
using Auth.Domain.Constants;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Common.Authentication;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using Auth_API.Modules.Authentication.Contracts;
using Auth_API.Modules.MachineCredentialChecks.Contracts;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth_API.Modules.MachineCredentialChecks.Controllers;

/// <summary>
/// An application's own server checking a credential presented to it (OI-128 PR B): an API key,
/// a webhook key, or a token (RFC 7662). It asks with its machine key, never a user's password,
/// and hears only about its own credentials.
/// </summary>
/// <remarks>
/// <para>
/// The second place the machine scheme is named (the first is the machine directory), at class
/// level. Nothing else guards these actions on purpose: no permission attribute, which would read
/// a token claim a machine token does not carry; no anonymous access; no other scheme. Each action
/// requires its own machine scope, read by the handler from the token.
/// </para>
/// <para>
/// The machine directory's answers: an application that is gone or inactive is refused exactly as
/// a revoked token is; a missing scope is a 403 with <c>error="insufficient_scope"</c> naming the
/// action's scope. A key of another application is answered as an unknown key, and a token not
/// issued to the caller as <c>{"active": false}</c>. Every answer carries
/// <c>Cache-Control: no-store</c>, set before the limiter by
/// <see cref="ApplicationReadClassificationMiddleware"/>, which also counts each request on its
/// application's budget at its address.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/app/machine")]
[Authorize(AuthenticationSchemes = AccessTokenValidation.MachineScheme)]
public sealed class MachineCredentialChecksController : ApiController
{
    private readonly ISender _sender;

    public MachineCredentialChecksController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Validates an API key presented to the calling application: its metadata when it is one of
    /// the application's active keys, otherwise <c>ApiKey.Invalid</c>.
    /// </summary>
    [HttpPost("apikeys/validate")]
    [EnableRateLimiting(ApplicationReadClassificationMiddleware.CredentialCheckPolicyName)]
    [MachineCredentialCheck(MachineClients.Scopes.ApiKeysValidate)]
    [SameAnswerAsBlacklist]
    [ProducesResponseType(typeof(ValidateApiKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request, CancellationToken cancellationToken) =>
        Answer(
            await _sender.Send(new ValidateApiKeyQuery(MachineCredentialCaller.FromPrincipal(User), request.ApiKey), cancellationToken),
            MachineClients.Scopes.ApiKeysValidate);

    /// <summary>
    /// Validates a webhook key presented to the calling application: its metadata when it is one
    /// of the application's active keys, otherwise <c>WebhookKey.Invalid</c>.
    /// </summary>
    [HttpPost("webhookkeys/validate")]
    [EnableRateLimiting(ApplicationReadClassificationMiddleware.CredentialCheckPolicyName)]
    [MachineCredentialCheck(MachineClients.Scopes.WebhookKeysValidate)]
    [SameAnswerAsBlacklist]
    [ProducesResponseType(typeof(ValidateWebhookKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ValidateWebhookKey([FromBody] ValidateWebhookKeyRequest request, CancellationToken cancellationToken) =>
        Answer(
            await _sender.Send(new ValidateWebhookKeyQuery(MachineCredentialCaller.FromPrincipal(User), request.WebhookKey), cancellationToken),
            MachineClients.Scopes.WebhookKeysValidate);

    /// <summary>
    /// Introspects a token (RFC 7662): <c>active: true</c> only for an access or refresh token
    /// issued to the calling application and still valid; <c>{"active": false}</c> for anything
    /// else.
    /// </summary>
    [HttpPost("tokens/introspect")]
    [EnableRateLimiting(ApplicationReadClassificationMiddleware.CredentialCheckPolicyName)]
    [MachineCredentialCheck(MachineClients.Scopes.TokensIntrospect)]
    [SameAnswerAsBlacklist]
    // The media type RFC 7662 mandates.
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(typeof(IntrospectTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> IntrospectToken([FromForm] IntrospectTokenRequest request, CancellationToken cancellationToken) =>
        Answer(
            await _sender.Send(
                new IntrospectTokenQuery(MachineCredentialCaller.FromPrincipal(User), request.Token, request.ParsedTokenTypeHint),
                cancellationToken),
            MachineClients.Scopes.TokensIntrospect);

    /// <summary>
    /// The answer for a check: 200; the revoked-token 401 for an application that can no longer be
    /// served; the scope challenge with a missing scope's 403; otherwise the error contract's
    /// problem.
    /// </summary>
    private IActionResult Answer<TValue>(ErrorOr<TValue> result, string requiredScope)
    {
        if (!result.IsError)
        {
            return Ok(result.Value);
        }

        var first = result.FirstError;

        if (first.Code == ApplicationDirectoryRefusals.CallerNotServable.Code)
        {
            BearerTokenRejection.Reject(HttpContext, ChallengeReasonCodes.TokenRevoked);
            return new EmptyResult();
        }

        if (first.Code == ApplicationDirectoryRefusals.InsufficientScope.Code)
        {
            // RFC 6750 §3.1: the scope the request would have needed.
            Response.Headers.WWWAuthenticate = $"Bearer error=\"insufficient_scope\", scope=\"{requiredScope}\"";
        }

        return Problem(result.Errors);
    }
}
